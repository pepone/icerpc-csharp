// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Compiler;

namespace IceRpc.SlicecGen;

/// <summary>Resolves Slice TypeRef to C# type strings and generates encode/decode expressions.</summary>
internal static class TypeResolver
{
    private static readonly Dictionary<string, (string CsType, string Suffix, bool IsValueType)> PrimitiveMap = new()
    {
        ["bool"] = ("bool", "Bool", true),
        ["int8"] = ("sbyte", "Int8", true),
        ["uint8"] = ("byte", "UInt8", true),
        ["int16"] = ("short", "Int16", true),
        ["uint16"] = ("ushort", "UInt16", true),
        ["int32"] = ("int", "Int32", true),
        ["uint32"] = ("uint", "UInt32", true),
        ["varint32"] = ("int", "VarInt32", true),
        ["varuint32"] = ("uint", "VarUInt32", true),
        ["int64"] = ("long", "Int64", true),
        ["uint64"] = ("ulong", "UInt64", true),
        ["varint62"] = ("long", "VarInt62", true),
        ["varuint62"] = ("ulong", "VarUInt62", true),
        ["float32"] = ("float", "Float32", true),
        ["float64"] = ("double", "Float64", true),
        ["string"] = ("string", "String", false),
    };

    /// <summary>Gets the C# type for a primitive Slice type, or null if not a primitive.</summary>
    internal static string? PrimitiveCsType(string typeId) =>
        PrimitiveMap.TryGetValue(typeId, out var info) ? info.CsType : null;

    /// <summary>Gets the encode/decode suffix for a primitive (e.g., "Int32" for encoder.EncodeInt32).</summary>
    internal static string? PrimitiveEncodeSuffix(string typeId) =>
        PrimitiveMap.TryGetValue(typeId, out var info) ? info.Suffix : null;

    /// <summary>Checks if a primitive type is a C# value type.</summary>
    internal static bool IsPrimitiveValueType(string typeId) =>
        PrimitiveMap.TryGetValue(typeId, out var info) && info.IsValueType;

    /// <summary>Resolves a TypeRef to its C# type string for field declarations.</summary>
    internal static string FieldTypeString(
        TypeRef typeRef,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string baseType = ResolveBaseType(typeRef.TypeId, file, currentNamespace, registry);
        return SetOptionalModifier(baseType, typeRef.IsOptional, IsValueType(typeRef, file, registry));
    }

    /// <summary>Checks if a TypeRef represents a C# value type.</summary>
    internal static bool IsValueType(TypeRef typeRef, SliceFile file, TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        // Primitives
        if (PrimitiveMap.TryGetValue(typeId, out var info))
        {
            return info.IsValueType;
        }

        // Anonymous and named types.
        return registry.FindSymbol(typeId, file) switch
        {
            Symbol.Struct => true,
            Symbol.Enum enumSymbol => !enumSymbol.V.IsUnchecked, // Checked enums map to C# enum (value type)
            _ => false, // Sequences, dictionaries, results, unknown types are reference types.
        };
    }

    /// <summary>Generates encode code for a non-tagged field.</summary>
    internal static string EncodeField(
        Field field,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string fieldName = CsNaming.FieldName(field);
        string param = $"this.{fieldName}";
        return EncodeExpression(field.DataType, file, currentNamespace, param, registry);
    }

    /// <summary>Generates decode expression for a non-tagged field.</summary>
    internal static string DecodeField(
        Field field,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry) =>
        DecodeExpression(field.DataType, file, currentNamespace, registry);

    /// <summary>Generates encode code for a tagged field.</summary>
    internal static string EncodeTaggedField(
        Field field,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string fieldName = CsNaming.FieldName(field);
        string param = $"this.{fieldName}";
        int tag = field.Tag!.Value;

        bool isValueType = IsValueType(field.DataType, file, registry);
        string csType = ResolveBaseType(field.DataType.TypeId, file, currentNamespace, registry);
        string varName = $"{CsNaming.FieldParameterName(field)}_";
        string encodeLambda = GetEncodeLambda(field.DataType, file, currentNamespace, registry);

        if (isValueType)
        {
            int? fixedSize = GetFixedSize(field.DataType);
            string encodeCall = fixedSize.HasValue
                ? $"encoder.EncodeTagged({tag}, size: {fixedSize.Value}, {varName}, {encodeLambda});"
                : $"encoder.EncodeTagged({tag}, {varName}, {encodeLambda});";
            return
                $"if ({param} is {csType} {varName})\n" +
                "{\n" +
                $"    {encodeCall}\n" +
                "}";
        }
        else
        {
            return
                $"if ({param} is {csType} {varName})\n" +
                "{\n" +
                $"    encoder.EncodeTagged({tag}, {varName}, {encodeLambda});\n" +
                "}";
        }
    }

    /// <summary>Generates decode expression for a tagged field.</summary>
    internal static string DecodeTaggedField(
        Field field,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        int tag = field.Tag!.Value;
        string decodeExpr = DecodeExpression(field.DataType, file, currentNamespace, registry);
        string csType = ResolveBaseType(field.DataType.TypeId, file, currentNamespace, registry);
        return $"decoder.DecodeTagged({tag}, (ref SliceDecoder decoder) => ({csType}?){decodeExpr})";
    }

    private static string ResolveBaseType(
        string typeId,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        // Check primitives first.
        if (PrimitiveCsType(typeId) is string csType)
        {
            return csType;
        }

        // Anonymous and named types.
        return registry.FindSymbol(typeId, file) switch
        {
            Symbol.SequenceType seq =>
                $"global::System.Collections.Generic.IList<{FieldTypeString(seq.V.ElementType, file, currentNamespace, registry)}>",
            Symbol.DictionaryType dict =>
                $"global::System.Collections.Generic.IDictionary<{FieldTypeString(dict.V.KeyType, file, currentNamespace, registry)}, {FieldTypeString(dict.V.ValueType, file, currentNamespace, registry)}>",
            Symbol.ResultType result =>
                $"Result<{FieldTypeString(result.V.SuccessType, file, currentNamespace, registry)}, {FieldTypeString(result.V.FailureType, file, currentNamespace, registry)}>",
            // User-defined type: scoped identifier like "Module::TypeName"
            _ => ResolveUserTypeName(typeId, currentNamespace),
        };
    }

    private static string ResolveUserTypeName(string typeId, string currentNamespace)
    {
        string[] parts = typeId.Split("::");
        string typeName = CsNaming.EscapeKeyword(CsNaming.ToPascalCase(parts[^1]));

        if (parts.Length == 1)
        {
            // Same module — use unqualified name.
            return typeName;
        }

        // Build namespace from all parts except the last (which is the type name).
        string typeNamespace = string.Join(
            ".",
            parts[..^1].Select(s => CsNaming.EscapeKeyword(CsNaming.ToPascalCase(s))));

        return CsNaming.ScopedIdentifier(typeName, typeNamespace, currentNamespace);
    }

    private static string SetOptionalModifier(string typeString, bool isOptional, bool isValueType)
    {
        if (!isOptional)
        {
            return typeString;
        }
        // Both value types (Nullable<T>) and reference types (nullable annotation) get '?'.
        return $"{typeString}?";
    }

    private static string EncodeExpression(
        TypeRef typeRef,
        SliceFile file,
        string currentNamespace,
        string param,
        TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        // Primitive
        if (PrimitiveEncodeSuffix(typeId) is string suffix)
        {
            return $"encoder.Encode{suffix}({param});";
        }

        // Anonymous and named types.
        return registry.FindSymbol(typeId, file) switch
        {
            Symbol.SequenceType seq => EncodeSequence(seq.V, file, currentNamespace, param, registry),
            Symbol.DictionaryType dict => EncodeDictionary(dict.V, file, currentNamespace, param, registry),
            Symbol.Enum enumSymbol when !enumSymbol.V.IsUnchecked =>
                $"{GetEncoderExtensionsClass(typeId)}.Encode{CsNaming.ToPascalCase(typeId.Split("::")[^1])}(ref encoder, {param});",
            _ => $"{param}.Encode(ref encoder);",
        };
    }

    private static string DecodeExpression(
        TypeRef typeRef,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        // Primitive
        if (PrimitiveEncodeSuffix(typeId) is string suffix)
        {
            return $"decoder.Decode{suffix}()";
        }

        // Anonymous and named types.
        string csType = ResolveUserTypeName(typeId, currentNamespace);
        return registry.FindSymbol(typeId, file) switch
        {
            Symbol.SequenceType seq => DecodeSequence(seq.V, file, currentNamespace, registry),
            Symbol.DictionaryType dict => DecodeDictionary(dict.V, file, currentNamespace, registry),
            Symbol.Enum enumSymbol when !enumSymbol.V.IsUnchecked =>
                $"{GetDecoderExtensionsClass(typeId)}.Decode{CsNaming.ToPascalCase(typeId.Split("::")[^1])}(ref decoder)",
            _ => $"new {csType}(ref decoder)",
        };
    }

    private static string EncodeSequence(
        SequenceType seq,
        SliceFile file,
        string currentNamespace,
        string param,
        TypeRegistry registry)
    {
        string elementEncodeLambda = GetEncodeLambda(seq.ElementType, file, currentNamespace, registry);
        return $"encoder.EncodeSequence({param}, {elementEncodeLambda});";
    }

    private static string DecodeSequence(
        SequenceType seq,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string elementDecodeLambda = GetDecodeLambda(seq.ElementType, file, currentNamespace, registry);
        return $"decoder.DecodeSequence({elementDecodeLambda})";
    }

    private static string EncodeDictionary(
        DictionaryType dict,
        SliceFile file,
        string currentNamespace,
        string param,
        TypeRegistry registry)
    {
        string keyEncodeLambda = GetEncodeLambda(dict.KeyType, file, currentNamespace, registry);
        string valueEncodeLambda = GetEncodeLambda(dict.ValueType, file, currentNamespace, registry);
        return $"encoder.EncodeDictionary({param}, {keyEncodeLambda}, {valueEncodeLambda});";
    }

    private static string DecodeDictionary(
        DictionaryType dict,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string keyDecodeLambda = GetDecodeLambda(dict.KeyType, file, currentNamespace, registry);
        string valueDecodeLambda = GetDecodeLambda(dict.ValueType, file, currentNamespace, registry);
        return $"decoder.DecodeDictionary({keyDecodeLambda}, {valueDecodeLambda})";
    }

    private static string GetEncodeLambda(
        TypeRef typeRef,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        if (PrimitiveEncodeSuffix(typeId) is string suffix)
        {
            return $"(ref SliceEncoder encoder, {PrimitiveCsType(typeId)} value) => encoder.Encode{suffix}(value)";
        }

        string csType = FieldTypeString(typeRef, file, currentNamespace, registry);
        return $"(ref SliceEncoder encoder, {csType} value) => value.Encode(ref encoder)";
    }

    private static string GetDecodeLambda(
        TypeRef typeRef,
        SliceFile file,
        string currentNamespace,
        TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        if (PrimitiveEncodeSuffix(typeId) is string suffix)
        {
            return $"(ref SliceDecoder decoder) => decoder.Decode{suffix}()";
        }

        string csType = ResolveBaseType(typeId, file, currentNamespace, registry);
        return $"(ref SliceDecoder decoder) => new {csType}(ref decoder)";
    }

    private static string GetEncoderExtensionsClass(string typeId)
    {
        string typeName = CsNaming.ToPascalCase(typeId.Split("::")[^1]);
        return $"{typeName}SliceEncoderExtensions";
    }

    private static string GetDecoderExtensionsClass(string typeId)
    {
        string typeName = CsNaming.ToPascalCase(typeId.Split("::")[^1]);
        return $"{typeName}SliceDecoderExtensions";
    }

    /// <summary>Gets the fixed encoded size of a type, or null if variable-size.</summary>
    private static int? GetFixedSize(TypeRef typeRef)
    {
        string typeId = typeRef.TypeId;

        return typeId switch
        {
            "bool" or "int8" or "uint8" => 1,
            "int16" or "uint16" => 2,
            "int32" or "uint32" or "float32" => 4,
            "int64" or "uint64" or "float64" => 8,
            _ => null, // variable-size or user-defined types (compute later if needed)
        };
    }
}
