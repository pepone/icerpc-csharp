// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Compiler;

namespace IceRpc.SlicecGen;

/// <summary>Generates C# record structs from Slice struct definitions.</summary>
internal static class StructGenerator
{
    /// <summary>Generates a complete C# record struct declaration.</summary>
    internal static CodeBlock GenerateStruct(Struct structDef, SliceFile file, TypeRegistry registry)
    {
        string escapedIdentifier = CsNaming.EscapedIdentifier(structDef.EntityInfo);
        string currentNamespace = CsNaming.AsNamespace(file.ModuleDeclaration);
        string accessModifier = CsNaming.AccessModifier(structDef.EntityInfo);
        bool isReadonly = CsNaming.HasAttribute(structDef.EntityInfo.Attributes, "cs::readonly");

        // Build the declaration prefix.
        string declaration = isReadonly
            ? $"{accessModifier} readonly partial record struct"
            : $"{accessModifier} partial record struct";

        var builder = new ContainerBuilder(declaration, escapedIdentifier);

        // Add doc comments.
        // TODO: format doc comment from structDef.EntityInfo.Comment

        string moduleScope = file.ModuleDeclaration.Identifier;
        string scopedId = string.IsNullOrEmpty(moduleScope)
            ? structDef.EntityInfo.Identifier
            : $"{moduleScope}::{structDef.EntityInfo.Identifier}";
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this record struct from the Slice struct <c>{scopedId}</c>.");

        // Add property declarations (in original order).
        CodeBlock fieldDeclarations = CodeBlock.FromBlocks(
            structDef.Fields.Select(
                f => FieldHelpers.FieldDeclaration(f, file, currentNamespace, accessModifier, isReadonly, registry)));
        builder.AddBlock(fieldDeclarations);

        // Add main constructor.
        builder.AddBlock(GenerateMainConstructor(
            structDef, file, currentNamespace, escapedIdentifier, accessModifier, registry));

        // Add decode constructor.
        builder.AddBlock(GenerateDecodeConstructor(
            structDef, file, currentNamespace, escapedIdentifier, accessModifier, registry));

        // Add encode method.
        builder.AddBlock(GenerateEncodeMethod(structDef, file, currentNamespace, accessModifier, registry));

        return builder.Build();
    }

    private static CodeBlock GenerateMainConstructor(
        Struct structDef,
        SliceFile file,
        string currentNamespace,
        string escapedIdentifier,
        string accessModifier,
        TypeRegistry registry)
    {
        bool hasRequiredField = structDef.Fields.Any(f => FieldHelpers.IsRequired(f, file, registry));

        var ctor = new FunctionBuilder(accessModifier, "", escapedIdentifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment("summary", $"Constructs a new instance of <see cref=\"{escapedIdentifier}\" />.");

        foreach (Field field in structDef.Fields)
        {
            string typeString = TypeResolver.FieldTypeString(field.DataType, file, currentNamespace, registry);
            string paramName = CsNaming.FieldParameterName(field);
            ctor.AddParameter(typeString, paramName);
        }

        var body = new CodeBlock();
        foreach (Field field in structDef.Fields)
        {
            body.WriteLine($"this.{CsNaming.FieldName(field)} = {CsNaming.FieldParameterName(field)};");
        }
        ctor.SetBody(body);

        return ctor.Build();
    }

    private static CodeBlock GenerateDecodeConstructor(
        Struct structDef,
        SliceFile file,
        string currentNamespace,
        string escapedIdentifier,
        string accessModifier,
        TypeRegistry registry)
    {
        IReadOnlyList<Field> sortedFields = FieldHelpers.GetSortedFields(structDef.Fields);
        bool hasRequiredField = structDef.Fields.Any(f => FieldHelpers.IsRequired(f, file, registry));

        var ctor = new FunctionBuilder(accessModifier, "", escapedIdentifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment(
            "summary",
            $"Constructs a new instance of <see cref=\"{escapedIdentifier}\" /> and decodes its fields from a Slice decoder.");
        ctor.AddComment("param", "name", "decoder", "The Slice decoder.");
        ctor.AddParameter("ref SliceDecoder", "decoder");

        var body = new CodeBlock();

        int bitSequenceSize = FieldHelpers.GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string fieldName = CsNaming.FieldName(field);

            if (FieldHelpers.IsTagged(field))
            {
                string decodeExpr = TypeResolver.DecodeTaggedField(field, file, currentNamespace, registry);
                body.WriteLine($"this.{fieldName} = {decodeExpr};");
            }
            else if (field.DataType.IsOptional)
            {
                // Non-tagged optional: use bit sequence reader.
                string decodeExpr = TypeResolver.DecodeField(field, file, currentNamespace, registry);
                body.WriteLine($"this.{fieldName} = bitSequenceReader.Read() ? {decodeExpr} : null;");
            }
            else
            {
                string decodeExpr = TypeResolver.DecodeField(field, file, currentNamespace, registry);
                body.WriteLine($"this.{fieldName} = {decodeExpr};");
            }
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("decoder.SkipTagged();");
        }

        ctor.SetBody(body);
        return ctor.Build();
    }

    private static CodeBlock GenerateEncodeMethod(
        Struct structDef,
        SliceFile file,
        string currentNamespace,
        string accessModifier,
        TypeRegistry registry)
    {
        IReadOnlyList<Field> sortedFields = FieldHelpers.GetSortedFields(structDef.Fields);

        var method = new FunctionBuilder(
            $"{accessModifier} readonly",
            "void",
            "Encode",
            FunctionType.BlockBody);

        method.AddComment("summary", "Encodes the fields of this struct with a Slice encoder.");
        method.AddComment("param", "name", "encoder", "The Slice encoder.");
        method.AddParameter("ref SliceEncoder", "encoder");

        var body = new CodeBlock();

        int bitSequenceSize = FieldHelpers.GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            if (FieldHelpers.IsTagged(field))
            {
                body.WriteLine(TypeResolver.EncodeTaggedField(field, file, currentNamespace, registry));
            }
            else if (field.DataType.IsOptional)
            {
                // Non-tagged optional: write bit and encode conditionally.
                string fieldName = CsNaming.FieldName(field);
                string param = $"this.{fieldName}";
                bool isValueType = TypeResolver.IsValueType(field.DataType, file, registry);

                body.WriteLine($"bitSequenceWriter.Write({param} != null);");
                string valueParam = isValueType ? $"{param}.Value" : param;
                string encodeExpr = EncodeValueExpression(field.DataType, valueParam, registry);
                body.WriteLine($"if ({param} != null)");
                body.WriteLine("{");
                body.WriteLine($"    {encodeExpr}");
                body.WriteLine("}");
            }
            else
            {
                body.WriteLine(TypeResolver.EncodeField(field, file, currentNamespace, registry));
            }
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }

        method.SetBody(body);
        return method.Build();
    }

    /// <summary>Generates an encode expression for use inside non-tagged optional if blocks.</summary>
    private static string EncodeValueExpression(TypeRef typeRef, string param, TypeRegistry registry)
    {
        string typeId = typeRef.TypeId;

        if (TypeResolver.PrimitiveEncodeSuffix(typeId) is string suffix)
        {
            return $"encoder.Encode{suffix}({param});";
        }

        // Checked enums use extension methods.
        Symbol? symbol = registry.FindSymbol(typeId);
        if (symbol is Symbol.Enum enumSymbol && !enumSymbol.V.IsUnchecked)
        {
            string typeName = CsNaming.ToPascalCase(typeId.Split("::")[^1]);
            return $"{typeName}SliceEncoderExtensions.Encode{typeName}(ref encoder, {param});";
        }

        return $"{param}.Encode(ref encoder);";
    }
}
