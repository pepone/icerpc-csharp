// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace IceRpc.SlicecGen;

/// <summary>Abstract base class for code generators. Owns the type registry (symbol-to-namespace mapping) and
/// provides type resolution and field helper methods.</summary>
internal class Generator
{
    protected static readonly Dictionary<BuiltinKind, (string CsType, string Suffix, bool IsValueType)> BuiltinMap = new()
    {
        [BuiltinKind.Bool] = ("bool", "Bool", true),
        [BuiltinKind.Int8] = ("sbyte", "Int8", true),
        [BuiltinKind.UInt8] = ("byte", "UInt8", true),
        [BuiltinKind.Int16] = ("short", "Int16", true),
        [BuiltinKind.UInt16] = ("ushort", "UInt16", true),
        [BuiltinKind.Int32] = ("int", "Int32", true),
        [BuiltinKind.UInt32] = ("uint", "UInt32", true),
        [BuiltinKind.VarInt32] = ("int", "VarInt32", true),
        [BuiltinKind.VarUInt32] = ("uint", "VarUInt32", true),
        [BuiltinKind.Int64] = ("long", "Int64", true),
        [BuiltinKind.UInt64] = ("ulong", "UInt64", true),
        [BuiltinKind.VarInt62] = ("long", "VarInt62", true),
        [BuiltinKind.VarUInt62] = ("ulong", "VarUInt62", true),
        [BuiltinKind.Float32] = ("float", "Float32", true),
        [BuiltinKind.Float64] = ("double", "Float64", true),
        [BuiltinKind.String] = ("string", "String", false),
    };

    private readonly Dictionary<Symbol, string> _namespaces = new();

    protected Generator(ImmutableList<SliceFile> symbolFiles)
    {
        foreach (SliceFile file in symbolFiles)
        {
            string ns = AsNamespace(file.Module);
            foreach (Symbol symbol in file.Contents)
            {
                _namespaces[symbol] = ns;
            }
        }
    }

    /// <summary>Gets the access modifier for an entity ("public" or "internal").</summary>
    protected static string AccessModifier(EntityInfo entity) =>
        entity.Attributes.HasAttribute(Attribute.CsInternal) ? "internal" : "public";

    // -- Naming helpers --

    /// <summary>Converts a Module to a C# namespace string (respects cs::namespace attribute).</summary>
    protected static string AsNamespace(Module module)
    {
        if (string.IsNullOrEmpty(module.Identifier))
        {
            return "";
        }

        if (module.Attributes.FindAttribute(Attribute.CsNamespace) is { } attr)
        {
            return attr.Args[0];
        }

        // Convert "Foo::Bar::Baz" to "Foo.Bar.Baz" with PascalCase on each segment.
        string[] segments = module.Identifier.Split("::");
        return string.Join(".", segments.Select(s => EntityInfoExtensions.EscapeKeyword(s.ToPascalCase())));
    }

    /// <summary>Qualifies a type name relative to the current namespace.</summary>
    protected static string ScopedIdentifier(string identifier, string identifierNamespace, string currentNamespace) =>
        currentNamespace == identifierNamespace
            ? identifier
            : $"global::{identifierNamespace}.{identifier}";

    // -- Type resolution methods --

    /// <summary>Resolves a TypeRef to its C# type string for field declarations.</summary>
    protected string FieldTypeString(TypeRef typeRef, string currentNamespace)
    {
        string baseType = ResolveBaseType(typeRef.Symbol, currentNamespace);
        return typeRef.IsOptional ? $"{baseType}?" : baseType;
    }

    /// <summary>Gets the EntityInfo from a named symbol, or null for anonymous/builtin symbols.</summary>
    protected static EntityInfo? GetEntityInfo(Symbol symbol) => symbol switch
    {
        Struct s => s.EntityInfo,
        EnumWithUnderlying e => e.EntityInfo,
        EnumWithFields e => e.EntityInfo,
        Interface i => i.EntityInfo,
        CustomType c => c.EntityInfo,
        TypeAlias t => t.EntityInfo,
        _ => null,
    };

    /// <summary>Generates encode code for a non-tagged field.</summary>
    protected string EncodeField(Field field, string currentNamespace)
    {
        string fieldName = field.FieldName;
        string param = $"this.{fieldName}";
        return EncodeExpression(field.Type, currentNamespace, param);
    }

    /// <summary>Generates decode expression for a non-tagged field.</summary>
    protected string DecodeField(Field field, string currentNamespace) =>
        DecodeExpression(field.Type, currentNamespace);

    /// <summary>Generates encode code for a tagged field.</summary>
    protected string EncodeTaggedField(Field field, string currentNamespace)
    {
        string fieldName = field.FieldName;
        string param = $"this.{fieldName}";
        int tag = field.Tag!.Value;

        bool isValueType = field.Type.IsValueType;
        string csType = ResolveBaseType(field.Type.Symbol, currentNamespace);
        string varName = $"{field.ParameterName}_";
        string encodeLambda = GetEncodeLambda(field.Type, currentNamespace);

        if (isValueType)
        {
            int? fixedSize = GetFixedSize(field.Type);
            string encodeCall = fixedSize.HasValue
                ? $"encoder.EncodeTagged({tag}, size: {fixedSize.Value}, {varName}, {encodeLambda});"
                : $"encoder.EncodeTagged({tag}, {varName}, {encodeLambda});";
            return @$"if ({param} is {csType} {varName})
{{
    {encodeCall}
}}";
        }
        else
        {
            return @$"if ({param} is {csType} {varName})
{{
    encoder.EncodeTagged({tag}, {varName}, {encodeLambda});
}}";
        }
    }

    /// <summary>Generates decode expression for a tagged field.</summary>
    protected string DecodeTaggedField(Field field, string currentNamespace)
    {
        int tag = field.Tag!.Value;
        string decodeExpr = DecodeExpression(field.Type, currentNamespace);
        string csType = ResolveBaseType(field.Type.Symbol, currentNamespace);
        return $"decoder.DecodeTagged({tag}, (ref SliceDecoder decoder) => ({csType}?){decodeExpr})";
    }

    /// <summary>Gets the full decode expression for a field, handling tagged, optional, and regular fields.</summary>
    protected string GetFieldDecodeExpression(Field field, string currentNamespace)
    {
        if (field.IsTagged)
        {
            return DecodeTaggedField(field, currentNamespace);
        }
        else if (field.Type.IsOptional)
        {
            string decodeExpr = DecodeField(field, currentNamespace);
            return $"bitSequenceReader.Read() ? {decodeExpr} : null";
        }
        else
        {
            return DecodeField(field, currentNamespace);
        }
    }

    // -- Field helpers --

    /// <summary>Returns fields sorted: non-tagged in original order, then tagged sorted by tag value.</summary>
    protected static IReadOnlyList<Field> GetSortedFields(ImmutableList<Field> fields)
    {
        var nonTagged = fields.Where(f => !f.IsTagged).ToList();
        var tagged = fields.Where(f => f.IsTagged).OrderBy(f => f.Tag!.Value).ToList();
        nonTagged.AddRange(tagged);
        return nonTagged;
    }

    /// <summary>Counts non-tagged optional fields (for Slice2 bit sequence sizing).</summary>
    protected static int GetBitSequenceSize(ImmutableList<Field> fields) =>
        fields.Count(f => !f.IsTagged && f.Type.IsOptional);

    /// <summary>Generates a property declaration for a field.</summary>
    protected CodeBlock FieldDeclaration(
        Field field,
        string currentNamespace,
        string accessModifier,
        bool parentReadonly)
    {
        var code = new CodeBlock();

        // cs::attribute
        foreach (var attr in field.EntityInfo.Attributes.CsAttributes())
        {
            code.WriteLine($"[{attr.Args[0]}]");
        }

        string typeString = FieldTypeString(field.Type, currentNamespace);
        string fieldName = field.FieldName;
        string required = field.IsRequired ? "required " : "";
        bool fieldReadonly = field.EntityInfo.Attributes.HasAttribute(Attribute.CsReadonly);
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }

    // -- Private type resolution helpers --

    private string ResolveBaseType(Symbol symbol, string currentNamespace)
    {
        if (symbol is Builtin builtin && BuiltinMap.TryGetValue(builtin.Kind, out var info))
        {
            return info.CsType;
        }

        return symbol switch
        {
            SequenceType seq =>
                $"global::System.Collections.Generic.IList<{FieldTypeString(seq.ElementType, currentNamespace)}>",
            DictionaryType dict =>
                $"global::System.Collections.Generic.IDictionary<{FieldTypeString(dict.KeyType, currentNamespace)}, {FieldTypeString(dict.ValueType, currentNamespace)}>",
            ResultType result =>
                $"Result<{FieldTypeString(result.SuccessType, currentNamespace)}, {FieldTypeString(result.FailureType, currentNamespace)}>",
            _ => ResolveUserTypeName(symbol, currentNamespace),
        };
    }

    private string ResolveUserTypeName(Symbol symbol, string currentNamespace)
    {
        EntityInfo entityInfo = GetEntityInfo(symbol)
            ?? throw new InvalidOperationException($"Symbol '{symbol.GetType().Name}' does not have an EntityInfo.");

        string typeName = entityInfo.EscapedName;

        if (_namespaces.TryGetValue(symbol, out string? typeNamespace) && typeNamespace != currentNamespace)
        {
            return ScopedIdentifier(typeName, typeNamespace, currentNamespace);
        }

        return typeName;
    }


    protected string EncodeExpression(TypeRef typeRef, string currentNamespace, string param)
    {
        if (typeRef.Symbol is Builtin builtin && BuiltinMap.TryGetValue(builtin.Kind, out var info))
        {
            return $"encoder.Encode{info.Suffix}({param});";
        }

        return typeRef.Symbol switch
        {
            SequenceType seq => EncodeSequence(seq, currentNamespace, param),
            DictionaryType dict => EncodeDictionary(dict, currentNamespace, param),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetEncoderExtensionsClass(e.EntityInfo)}.Encode{e.EntityInfo.EscapedName}(ref encoder, {param});",
            EnumWithFields e =>
                $"{GetEncoderExtensionsClass(e.EntityInfo)}.Encode{e.EntityInfo.EscapedName}(ref encoder, {param});",
            _ => $"{param}.Encode(ref encoder);",
        };
    }

    private string DecodeExpression(TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Symbol is Builtin builtin && BuiltinMap.TryGetValue(builtin.Kind, out var info))
        {
            return $"decoder.Decode{info.Suffix}()";
        }

        return typeRef.Symbol switch
        {
            SequenceType seq => DecodeSequence(seq, currentNamespace),
            DictionaryType dict => DecodeDictionary(dict, currentNamespace),
            EnumWithUnderlying e when !e.IsUnchecked =>
                $"{GetDecoderExtensionsClass(e.EntityInfo)}.Decode{e.EntityInfo.EscapedName}(ref decoder)",
            EnumWithFields e =>
                $"{GetDecoderExtensionsClass(e.EntityInfo)}.Decode{e.EntityInfo.EscapedName}(ref decoder)",
            _ => $"new {ResolveUserTypeName(typeRef.Symbol, currentNamespace)}(ref decoder)",
        };
    }

    private string EncodeSequence(SequenceType seq, string currentNamespace, string param)
    {
        string elementEncodeLambda = GetEncodeLambda(seq.ElementType, currentNamespace);
        return $"encoder.EncodeSequence({param}, {elementEncodeLambda});";
    }

    private string DecodeSequence(SequenceType seq, string currentNamespace)
    {
        string elementDecodeLambda = GetDecodeLambda(seq.ElementType, currentNamespace);
        return $"decoder.DecodeSequence({elementDecodeLambda})";
    }

    private string EncodeDictionary(DictionaryType dict, string currentNamespace, string param)
    {
        string keyEncodeLambda = GetEncodeLambda(dict.KeyType, currentNamespace);
        string valueEncodeLambda = GetEncodeLambda(dict.ValueType, currentNamespace);
        return $"encoder.EncodeDictionary({param}, {keyEncodeLambda}, {valueEncodeLambda});";
    }

    private string DecodeDictionary(DictionaryType dict, string currentNamespace)
    {
        string keyDecodeLambda = GetDecodeLambda(dict.KeyType, currentNamespace);
        string valueDecodeLambda = GetDecodeLambda(dict.ValueType, currentNamespace);
        return $"decoder.DecodeDictionary({keyDecodeLambda}, {valueDecodeLambda})";
    }

    private string GetEncodeLambda(TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Symbol is Builtin builtin && BuiltinMap.TryGetValue(builtin.Kind, out var info))
        {
            return $"(ref SliceEncoder encoder, {info.CsType} value) => encoder.Encode{info.Suffix}(value)";
        }

        string csType = FieldTypeString(typeRef, currentNamespace);
        if (typeRef.Symbol is EnumWithUnderlying or EnumWithFields)
        {
            EntityInfo entityInfo = GetEntityInfo(typeRef.Symbol)!;
            string encoderClass = GetEncoderExtensionsClass(entityInfo);
            string encodeName = entityInfo.EscapedName;
            return $"(ref SliceEncoder encoder, {csType} value) => {encoderClass}.Encode{encodeName}(ref encoder, value)";
        }

        return $"(ref SliceEncoder encoder, {csType} value) => value.Encode(ref encoder)";
    }

    private string GetDecodeLambda(TypeRef typeRef, string currentNamespace)
    {
        if (typeRef.Symbol is Builtin builtin && BuiltinMap.TryGetValue(builtin.Kind, out var info))
        {
            return $"(ref SliceDecoder decoder) => decoder.Decode{info.Suffix}()";
        }

        if (typeRef.Symbol is EnumWithUnderlying or EnumWithFields)
        {
            EntityInfo entityInfo = GetEntityInfo(typeRef.Symbol)!;
            string decoderClass = GetDecoderExtensionsClass(entityInfo);
            string decodeName = entityInfo.EscapedName;
            return $"(ref SliceDecoder decoder) => {decoderClass}.Decode{decodeName}(ref decoder)";
        }

        string csType = ResolveBaseType(typeRef.Symbol, currentNamespace);
        return $"(ref SliceDecoder decoder) => new {csType}(ref decoder)";
    }

    private static string GetEncoderExtensionsClass(EntityInfo entityInfo)
    {
        string typeName = entityInfo.EscapedName;
        return $"{typeName}SliceEncoderExtensions";
    }

    private static string GetDecoderExtensionsClass(EntityInfo entityInfo)
    {
        string typeName = entityInfo.EscapedName;
        return $"{typeName}SliceDecoderExtensions";
    }

    private static int? GetFixedSize(TypeRef typeRef) => typeRef.Symbol switch
    {
        Builtin b => b.Kind switch
        {
            BuiltinKind.Bool or BuiltinKind.Int8 or BuiltinKind.UInt8 => 1,
            BuiltinKind.Int16 or BuiltinKind.UInt16 => 2,
            BuiltinKind.Int32 or BuiltinKind.UInt32 or BuiltinKind.Float32 => 4,
            BuiltinKind.Int64 or BuiltinKind.UInt64 or BuiltinKind.Float64 => 8,
            _ => null,
        },
        _ => null,
    };
}
