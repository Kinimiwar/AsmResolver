using System;
using System.Collections.Generic;
using System.Threading;
using AsmResolver.DotNet.Blob;
using AsmResolver.DotNet.Collections;
using AsmResolver.PE.DotNet.Metadata;
using AsmResolver.PE.DotNet.Metadata.Guid;
using AsmResolver.PE.DotNet.Metadata.Strings;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using AsmResolver.PE.DotNet.Metadata.UserStrings;

namespace AsmResolver.DotNet.Serialized
{
    /// <summary>
    /// Represents a lazily initialized implementation of <see cref="ModuleDefinition"/>  that is read from a
    /// .NET metadata image. 
    /// </summary>
    public class SerializedModuleDefinition : ModuleDefinition
    {
        private readonly IMetadata _metadata;
        private readonly ModuleDefinitionRow _row;
        private readonly CachedSerializedMemberFactory _memberFactory;

        private IDictionary<uint, IList<uint>> _typeDefTree;
        private IDictionary<uint, uint> _parentTypeRids;

        private readonly LazyOneToManyTokenRelation<TypeDefinitionRow> _fieldLists;
        private readonly LazyOneToManyTokenRelation<TypeDefinitionRow> _methodLists;
        private readonly LazyOneToManyTokenRelation<MethodDefinitionRow> _paramLists;
        private readonly LazyOneToManyTokenRelation<PropertyMapRow> _propertyLists;
        private readonly LazyOneToManyTokenRelation<EventMapRow> _eventLists;
        
        /// <summary>
        /// Creates a module definition from a module metadata row.
        /// </summary>
        /// <param name="metadata">The object providing access to the underlying metadata streams.</param>
        /// <param name="token">The token to initialize the module for.</param>
        /// <param name="row">The metadata table row to base the module definition on.</param>
        public SerializedModuleDefinition(IMetadata metadata, MetadataToken token, ModuleDefinitionRow row)
            : base(token)
        {
            _metadata = metadata;
            _row = row;
            Generation = row.Generation;
            MetadataToken = token;

            _memberFactory = new CachedSerializedMemberFactory(metadata, this);
            CorLibTypeFactory = new CorLibTypeFactory(FindMostRecentCorLib());

            var tablesStream = metadata.GetStream<TablesStream>();

            _fieldLists = new LazyOneToManyTokenRelation<TypeDefinitionRow>(metadata, TableIndex.TypeDef,
                (rid, _) => rid, tablesStream.GetFieldRange);
            _methodLists = new LazyOneToManyTokenRelation<TypeDefinitionRow>(metadata, TableIndex.TypeDef, 
                (rid, _) => rid, tablesStream.GetMethodRange);
            _paramLists = new LazyOneToManyTokenRelation<MethodDefinitionRow>(metadata, TableIndex.Method, 
                (rid, _) => rid, tablesStream.GetParameterRange);
            _propertyLists = new LazyOneToManyTokenRelation<PropertyMapRow>(metadata, TableIndex.PropertyMap, 
                (_, map) => map.Parent, tablesStream.GetPropertyRange);
            _eventLists = new LazyOneToManyTokenRelation<EventMapRow>(metadata, TableIndex.EventMap,
                (_, map) => map.Parent, tablesStream.GetEventRange);
        }

        /// <inheritdoc />
        public override IMetadataMember LookupMember(MetadataToken token) =>
            !TryLookupMember(token, out var member)
                ? throw new ArgumentException($"Cannot resolve metadata token {token}.")
                : member;

        /// <inheritdoc />
        public override bool TryLookupMember(MetadataToken token, out IMetadataMember member) =>
            _memberFactory.TryLookupMember(token, out member);

        /// <inheritdoc />
        public override string LookupString(MetadataToken token) => 
            !TryLookupString(token, out var member)
                ? throw new ArgumentException($"Cannot resolve string token {token}.")
                : member;

        /// <inheritdoc />
        public override bool TryLookupString(MetadataToken token, out string value)
        {
            value = _metadata.GetStream<UserStringsStream>().GetStringByIndex(token.Rid);
            return value == null;
        }

        /// <inheritdoc />
        public override IndexEncoder GetIndexEncoder(CodedIndex codedIndex) =>
            _metadata.GetStream<TablesStream>().GetIndexEncoder(codedIndex);

        /// <inheritdoc />
        protected override string GetName() 
            => _metadata.GetStream<StringsStream>()?.GetStringByIndex(_row.Name);

        /// <inheritdoc />
        protected override Guid GetMvid() 
            => _metadata.GetStream<GuidStream>()?.GetGuidByIndex(_row.Mvid) ?? Guid.Empty;

        /// <inheritdoc />
        protected override Guid GetEncId()
            => _metadata.GetStream<GuidStream>()?.GetGuidByIndex(_row.EncId) ?? Guid.Empty;

        /// <inheritdoc />
        protected override Guid GetEncBaseId()
            => _metadata.GetStream<GuidStream>()?.GetGuidByIndex(_row.EncBaseId) ?? Guid.Empty;

        /// <inheritdoc />
        protected override IList<TypeDefinition> GetTopLevelTypes()
        {
            EnsureTypeDefinitionTreeInitialized();
            
            var types = new OwnedCollection<ModuleDefinition, TypeDefinition>(this);

            var typeDefTable = _metadata.GetStream<TablesStream>().GetTable<TypeDefinitionRow>(TableIndex.TypeDef);
            for (int i = 0; i < typeDefTable.Count; i++)
            {
                uint rid = (uint) i + 1;
                if (!_parentTypeRids.ContainsKey(rid))
                {
                    var token = new MetadataToken(TableIndex.TypeDef, rid);
                    types.Add(_memberFactory.LookupTypeDefinition(token));
                }
            }

            return types;
        }

        private void EnsureTypeDefinitionTreeInitialized()
        {
            if (_typeDefTree is null)
                InitializeTypeDefinitionTree();
        }

        private void InitializeTypeDefinitionTree()
        {
            var tablesStream = _metadata.GetStream<TablesStream>();
            var nestedClassTable = tablesStream.GetTable<NestedClassRow>(TableIndex.NestedClass);
            
            _typeDefTree = new Dictionary<uint, IList<uint>>();
            _parentTypeRids = new Dictionary<uint, uint>();
            
            foreach (var nestedClass in nestedClassTable)
            {
                _parentTypeRids.Add(nestedClass.NestedClass, nestedClass.EnclosingClass);
                var nestedTypeRids = GetNestedTypeRids(nestedClass.EnclosingClass);
                nestedTypeRids.Add(nestedClass.NestedClass);
            }
        }

        internal IList<uint> GetNestedTypeRids(uint enclosingTypeRid)
        {
            if (!_typeDefTree.TryGetValue(enclosingTypeRid, out var nestedRids))
            {
                nestedRids = new List<uint>();
                _typeDefTree.Add(enclosingTypeRid, nestedRids);
            }

            return nestedRids;
        }

        internal uint GetParentTypeRid(uint nestedTypeRid)
        {
            EnsureTypeDefinitionTreeInitialized();
            _parentTypeRids.TryGetValue(nestedTypeRid, out uint parentRid);
            return parentRid;
        }

        internal MetadataRange GetFieldRange(uint typeRid) => _fieldLists.GetMemberRange(typeRid);
        internal uint GetFieldDeclaringType(uint fieldRid) => _fieldLists.GetMemberOwner(fieldRid);
        internal MetadataRange GetMethodRange(uint typeRid) => _methodLists.GetMemberRange(typeRid);
        internal uint GetMethodDeclaringType(uint methodRid) => _methodLists.GetMemberOwner(methodRid);
        internal MetadataRange GetParameterRange(uint methodRid) => _paramLists.GetMemberRange(methodRid);
        internal uint GetParameterOwner(uint paramRid) => _paramLists.GetMemberOwner(paramRid);
        internal MetadataRange GetPropertyRange(uint typeRid) => _propertyLists.GetMemberRange(typeRid);
        internal uint GetPropertyDeclaringType(uint propertyRid) => _propertyLists.GetMemberOwner(propertyRid);
        internal MetadataRange GetEventRange(uint typeRid) => _eventLists.GetMemberRange(typeRid);
        internal uint GetEventDeclaringType(uint eventRid) => _eventLists.GetMemberOwner(eventRid);

        /// <inheritdoc />
        protected override IList<AssemblyReference> GetAssemblyReferences()
        {
            var result = new OwnedCollection<ModuleDefinition, AssemblyReference>(this);

            var table = _metadata.GetStream<TablesStream>().GetTable<AssemblyReferenceRow>();
            for (int i = 0; i < table.Count; i++)
            {
                var token = new MetadataToken(TableIndex.AssemblyRef, (uint) i + 1);
                result.Add(new SerializedAssemblyReference(_metadata, token, table[i]));
            }

            return result;
        }

        private IResolutionScope FindMostRecentCorLib()
        {
            // TODO: perhaps check public key tokens.
            
            IResolutionScope mostRecentCorLib = null;
            var mostRecentVersion = new Version();
            foreach (var reference in AssemblyReferences)
            {
                if (CorLibTypeFactory.KnownCorLibNames.Contains(reference.Name))
                {
                    if (mostRecentVersion < reference.Version)
                        mostRecentCorLib = reference;
                }
            }

            if (mostRecentCorLib is null)
            {
                if (CorLibTypeFactory.KnownCorLibNames.Contains(Assembly.Name))
                    mostRecentCorLib = this;
            }

            return mostRecentCorLib;
        }
    }
}