﻿using System;
using AsmResolver.Net.Emit;
using AsmResolver.Net.Cts;
using AsmResolver.Net.Metadata;

namespace AsmResolver.Net.Signatures
{
    /// <summary>
    /// Represents a single element in a custom attribute argument.
    /// </summary>
    public class ElementSignature : BlobSignature
    {
        /// <summary>
        /// Reads a single element at the current position of the provided stream reader.
        /// </summary>
        /// <param name="image">The image the custom attribute is defined in.</param>
        /// <param name="typeSignature">The type of the element to read.</param>
        /// <param name="reader">The reader to use.</param>
        /// <returns>The read element.</returns>
        public static ElementSignature FromReader(MetadataImage image, TypeSignature typeSignature, IBinaryStreamReader reader)
        {
            return new ElementSignature(ReadValue(image, typeSignature, reader));
        }

        private static object ReadValue(MetadataImage image, TypeSignature typeSignature, IBinaryStreamReader reader)
        {
            switch (typeSignature.ElementType)
            {
                case ElementType.Boolean:
                    return reader.ReadByte() == 1;
                case ElementType.Char:
                    return (char)reader.ReadUInt16();
                case ElementType.R4:
                    return reader.ReadSingle();
                case ElementType.R8:
                    return reader.ReadDouble();
                case ElementType.I1:
                    return reader.ReadSByte();
                case ElementType.I2:
                    return reader.ReadInt16();
                case ElementType.I4:
                    return reader.ReadInt32();
                case ElementType.I8:
                    return reader.ReadInt64();
                case ElementType.U1:
                    return reader.ReadByte();
                case ElementType.U2:
                    return reader.ReadUInt16();
                case ElementType.U4:
                    return reader.ReadUInt32();
                case ElementType.U8:
                    return reader.ReadUInt64();
                case ElementType.String:
                    return reader.ReadSerString();
                case ElementType.Object:
                    return ReadValue(image, TypeSignature.ReadFieldOrPropType(image, reader), reader);
                case ElementType.Class:
                case ElementType.Enum:
                case ElementType.ValueType:
                    var enumTypeDef = image.MetadataResolver.ResolveType(typeSignature);
                    if (enumTypeDef == null)
                        throw new MemberResolutionException(typeSignature);

                    if (enumTypeDef.IsEnum)
                        return ReadValue(image, enumTypeDef.GetEnumUnderlyingType(), reader);
                    break;
            }

            if (typeSignature.IsTypeOf("System", "Type"))
                return TypeSignature.FromAssemblyQualifiedName(image, reader.ReadSerString());

            throw new NotSupportedException("Unsupported element type " + typeSignature.ElementType);
        }

        public ElementSignature(object value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets or sets the value of the argument element.
        /// </summary>
        public object Value
        {
            get;
            set;
        }

        /// <inheritdoc />
        public override uint GetPhysicalLength(MetadataBuffer buffer)
        {
            if (Value == null)
                return 1;

            switch (Type.GetTypeCode(Value.GetType()))
            {
                case TypeCode.Boolean:
                case TypeCode.Byte:
                case TypeCode.SByte:
                    return sizeof(byte);
                case TypeCode.Char:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    return sizeof(ushort);
                case TypeCode.Single:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    return sizeof(uint);
                case TypeCode.Double:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return sizeof(ulong);
                case TypeCode.String:
                    return ((Value as string).GetSerStringSize());
            }

            if (Value is TypeSignature typeSignature)
                return TypeNameBuilder.GetAssemblyQualifiedName(typeSignature).GetSerStringSize();

            throw new NotSupportedException("Invalid or unsupported argument element value in custom attribute.");
        }

        /// <inheritdoc />
        public override void Prepare(MetadataBuffer buffer)
        {
        }

        /// <inheritdoc />
        public override void Write(MetadataBuffer buffer, IBinaryStreamWriter writer)
        {
            if (Value == null)
            {
                writer.WriteSerString(null);
                return;
            }

            switch (Type.GetTypeCode(Value.GetType()))
            {
                case TypeCode.Boolean:
                    writer.WriteByte((byte)((bool)Value ? 1 : 0));
                    break;
                case TypeCode.Byte:
                    writer.WriteByte((byte)Value);
                    break;
                case TypeCode.Char:
                    writer.WriteUInt16((char)Value);
                    break;
                case TypeCode.Double:
                    writer.WriteDouble((double)Value);
                    break;
                case TypeCode.Int16:
                    writer.WriteInt16((short)Value);
                    break;
                case TypeCode.Int32:
                    writer.WriteInt32((int)Value);
                    break;
                case TypeCode.Int64:
                    writer.WriteInt64((long)Value);
                    break;
                case TypeCode.SByte:
                    writer.WriteSByte((sbyte)Value);
                    break;
                case TypeCode.Single:
                    writer.WriteSingle((float)Value);
                    break;
                case TypeCode.String:
                    writer.WriteSerString((string)Value);
                    break;
                case TypeCode.UInt16:
                    writer.WriteUInt16((ushort)Value);
                    break;
                case TypeCode.UInt32:
                    writer.WriteUInt32((uint)Value);
                    break;
                case TypeCode.UInt64:
                    writer.WriteUInt64((ulong)Value);
                    break;
                default:
                    if (Value is TypeSignature typeSignature)
                        writer.WriteSerString(TypeNameBuilder.GetAssemblyQualifiedName(typeSignature));
                    else
                        throw new NotSupportedException();
                    break;
            }
        }
    }
}
