// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Generates Dunet discriminated unions from Slice enums with fields.</summary>
internal sealed class EnumWithFieldsGenerator : Generator
{
    internal EnumWithFieldsGenerator(ImmutableList<SliceFile> symbolFiles)
        : base(symbolFiles)
    {
    }

    internal CodeBlock Generate(EnumWithFields enumDef)
    {
        string escapedIdentifier = enumDef.EntityInfo.EscapedName;
        string accessModifier = AccessModifier(enumDef.EntityInfo);
        string currentNamespace = enumDef.EntityInfo.Namespace;

        return CodeBlock.FromBlocks(
        [
            GenerateUnionDeclaration(enumDef, escapedIdentifier, accessModifier, currentNamespace),
            GenerateEncoderExtensions(enumDef, escapedIdentifier, accessModifier),
            GenerateDecoderExtensions(enumDef, escapedIdentifier, accessModifier, currentNamespace),
        ]);
    }

    // -- Union declaration --

    private CodeBlock GenerateUnionDeclaration(
        EnumWithFields enumDef,
        string escapedIdentifier,
        string accessModifier,
        string currentNamespace)
    {
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} abstract partial record class",
            escapedIdentifier);

        builder.AddComment(
            "remarks",
            @$"The Slice compiler generated this discriminated union from the Slice enum <c>{scopedId}</c>.");
        builder.AddAttribute("Dunet.Union");

        // Generate nested record classes for each enumerator.
        int discriminant = 0;
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            builder.AddBlock(
                GenerateEnumeratorRecord(
                    enumerator,
                    enumDef,
                    escapedIdentifier,
                    accessModifier,
                    currentNamespace,
                    discriminant));
            discriminant++;
        }

        // For unchecked enums, add the Unknown variant.
        if (enumDef.IsUnchecked)
        {
            builder.AddBlock(GenerateUnknownRecord(enumDef, escapedIdentifier, accessModifier));
        }

        // Abstract Encode method.
        var abstractEncode = new CodeBlock();
        abstractEncode.WriteLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        abstractEncode.WriteLine("internal abstract void Encode(ref SliceEncoder encoder);");
        builder.AddBlock(abstractEncode);

        return builder.Build();
    }

    private CodeBlock GenerateEnumeratorRecord(
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
        string parentIdentifier,
        string accessModifier,
        string currentNamespace,
        int discriminant)
    {
        string enumeratorName = enumerator.EntityInfo.EscapedName;

        // Build parameter list for the record constructor.
        string nameWithParams = enumerator.Fields.Count > 0
            ? $"{enumeratorName}({BuildParameterList(enumerator.Fields, currentNamespace)})"
            : enumeratorName;

        var builder = new ContainerBuilder($"{accessModifier} partial record class", nameWithParams);
        builder.AddBase(parentIdentifier);

        // cs::attribute on the enumerator.
        builder.AddCsAttributes(enumerator.EntityInfo.Attributes);

        // Discriminant constant.
        var discriminantBlock = new CodeBlock();
        discriminantBlock.WriteLine("/// <summary>The discriminant of this enumerator, used for encoding/decoding.</summary>");
        discriminantBlock.WriteLine($"public const int Discriminant = {discriminant};");
        builder.AddBlock(discriminantBlock);

        // Encode method override.
        builder.AddBlock(GenerateEncodeMethod(enumerator, enumDef, currentNamespace));

        return builder.Build();
    }

    private static CodeBlock GenerateUnknownRecord(
        EnumWithFields enumDef,
        string parentIdentifier,
        string accessModifier)
    {
        string enumName = enumDef.EntityInfo.EscapedName;
        var builder = new ContainerBuilder(
            $"{accessModifier} partial record class",
            $"Unknown(int Discriminant, global::System.ReadOnlyMemory<byte> Fields)");
        builder.AddBase(parentIdentifier);
        builder.AddComment(
            "summary",
            @$"Represents an enumerator not defined in the local Slice definition of unchecked enum '{enumName}'.");
        builder.AddComment("param", "name", "Discriminant", "The discriminant of this unknown enumerator.");
        builder.AddComment("param", "name", "Fields", "The encoded fields of this unknown enumerator.");

        builder.AddBlock("""
            [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
            internal override void Encode(ref SliceEncoder encoder)
            {
                encoder.EncodeVarInt32(Discriminant);
                encoder.EncodeSize(Fields.Length);
                encoder.WriteByteSpan(Fields.Span);
            }
            """);

        return builder.Build();
    }

    private CodeBlock GenerateEncodeMethod(
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
        string currentNamespace)
    {
        IReadOnlyList<Field> sortedFields = GetSortedFields(enumerator.Fields);

        var code = new CodeBlock();
        code.WriteLine("""
            [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
            internal override void Encode(ref SliceEncoder encoder)
            {
                encoder.EncodeVarInt32(Discriminant);
            """);

        // For unchecked (non-compact) enums, add size placeholder.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("""
                    var sizePlaceholder = encoder.GetPlaceholderSpan(4);
                    int startPos = encoder.EncodedByteCount;
                """);
        }

        // Bit sequence for non-tagged optional fields.
        int bitSequenceSize = GetBitSequenceSize(enumerator.Fields);
        if (bitSequenceSize > 0)
        {
            code.WriteLine($"    var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        // Encode each field.
        foreach (Field field in sortedFields)
        {
            string param = $"this.{field.EntityInfo.EscapedName}";

            if (field.IsTagged)
            {
                string taggedExpr = EncodeTaggedField(field, currentNamespace);
                // Indent multi-line tagged field expressions.
                foreach (string line in taggedExpr.Split('\n'))
                {
                    code.WriteLine($"    {line}");
                }
            }
            else if (field.Type.IsOptional)
            {
                bool isValueType = field.Type.IsValueType;
                string valueParam = isValueType ? $"{param}.Value" : param;
                string encodeExpr = EncodeExpression(field.Type, currentNamespace, valueParam);
                code.WriteLine($$"""
                        bitSequenceWriter.Write({{param}} != null);
                        if ({{param}} != null)
                        {
                            {{encodeExpr}}
                        }
                    """);
            }
            else
            {
                string encodeExpr = EncodeExpression(field.Type, currentNamespace, param);
                code.WriteLine($"    {encodeExpr}");
            }
        }

        // Tag end marker for non-compact enums.
        if (!enumDef.IsCompact)
        {
            code.WriteLine("    encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }

        // Close size for unchecked enums.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("    SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);");
        }

        code.WriteLine("}");
        return code;
    }

    // -- Encoder extensions --

    private static CodeBlock GenerateEncoderExtensions(
        EnumWithFields enumDef,
        string escapedIdentifier,
        string accessModifier)
    {
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{escapedIdentifier}SliceEncoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for encoding a <see cref=""{escapedIdentifier}"" /> using a <see cref=""SliceEncoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.");

        var method = new FunctionBuilder(
            $"{accessModifier} static",
            "void",
            $"Encode{escapedIdentifier}",
            FunctionType.ExpressionBody);

        method.AddComment("summary", @$"Encodes a <see cref=""{escapedIdentifier}"" /> enum.");
        method.AddParameter("this ref SliceEncoder", "encoder", null, "The Slice encoder.");
        method.AddParameter(
            escapedIdentifier,
            "value",
            null,
            @$"The <see cref=""{escapedIdentifier}"" /> enumerator value to encode.");
        method.SetBody("value.Encode(ref encoder)");

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    // -- Decoder extensions --

    private CodeBlock GenerateDecoderExtensions(
        EnumWithFields enumDef,
        string escapedIdentifier,
        string accessModifier,
        string currentNamespace)
    {
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{escapedIdentifier}SliceDecoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for decoding a <see cref=""{escapedIdentifier}"" /> using a <see cref=""SliceDecoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.");

        var method = new FunctionBuilder(
            $"{accessModifier} static",
            escapedIdentifier,
            $"Decode{escapedIdentifier}",
            FunctionType.BlockBody);

        method.AddComment("summary", @$"Decodes a <see cref=""{escapedIdentifier}"" /> enum.");
        method.AddParameter("this ref SliceDecoder", "decoder", null, "The Slice decoder.");
        method.AddComment(
            "returns",
            @$"The decoded <see cref=""{escapedIdentifier}"" /> enumerator value.");

        var body = new CodeBlock();

        // Build the switch expression.
        body.WriteLine("return decoder.DecodeVarInt32() switch");
        body.WriteLine("{");
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            string enumeratorName = enumerator.EntityInfo.EscapedName;
            body.WriteLine(
                $"    {escapedIdentifier}.{enumeratorName}.Discriminant => Decode{enumeratorName}(ref decoder),");
        }

        // Fallback case.
        if (enumDef.IsUnchecked)
        {
            body.WriteLine(
                $"    int value => new {escapedIdentifier}.Unknown(value, decoder.DecodeSequence<byte>())");
        }
        else
        {
            body.WriteLine(
                @$"    int value => throw new global::System.IO.InvalidDataException($""Received invalid discriminant value '{{value}}' for {escapedIdentifier}."")");
        }
        body.WriteLine("};");

        // Local static decode functions for each enumerator.
        foreach (EnumWithFields.Enumerator enumerator in enumDef.Enumerators)
        {
            body.AddBlock(GenerateDecodeLocalFunction(enumerator, enumDef, escapedIdentifier, currentNamespace));
        }

        method.SetBody(body);
        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private CodeBlock GenerateDecodeLocalFunction(
        EnumWithFields.Enumerator enumerator,
        EnumWithFields enumDef,
        string parentIdentifier,
        string currentNamespace)
    {
        string enumeratorName = enumerator.EntityInfo.EscapedName;
        IReadOnlyList<Field> sortedFields = GetSortedFields(enumerator.Fields);

        var code = new CodeBlock();
        code.WriteLine($"static {parentIdentifier}.{enumeratorName} Decode{enumeratorName}(ref SliceDecoder decoder)");
        code.WriteLine("{");

        // For unchecked enums, skip the size prefix.
        if (enumDef.IsUnchecked)
        {
            code.WriteLine("    decoder.SkipSize();");
        }

        // Bit sequence for non-tagged optional fields.
        int bitSequenceSize = GetBitSequenceSize(enumerator.Fields);
        if (bitSequenceSize > 0)
        {
            code.WriteLine($"    var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        // Build the constructor call with named parameters.
        if (enumerator.Fields.Count == 0)
        {
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}();");
        }
        else if (sortedFields.Count == 1 && !sortedFields[0].IsTagged)
        {
            // Single non-tagged field, simple one-liner.
            Field field = sortedFields[0];
            string paramName = field.EntityInfo.EscapedName;
            string decodeExpr = GetFieldDecodeExpression(field, currentNamespace);
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}({paramName}: {decodeExpr});");
        }
        else
        {
            // Multi-field: build with named args.
            // Separate tagged and non-tagged for proper ordering.
            var nonTaggedFields = sortedFields.Where(f => !f.IsTagged).ToList();
            var taggedFields = sortedFields.Where(f => f.IsTagged).ToList();

            // For tagged fields, decode them into the constructor.
            // For non-tagged optional fields, use bitSequenceReader.
            code.WriteLine($"    var result = new {parentIdentifier}.{enumeratorName}(");
            var allFields = nonTaggedFields.Concat(taggedFields).ToList();
            for (int i = 0; i < allFields.Count; i++)
            {
                Field field = allFields[i];
                string paramName = field.EntityInfo.EscapedName;
                string decodeExpr = GetFieldDecodeExpression(field, currentNamespace);
                string separator = i < allFields.Count - 1 ? "," : ");";
                code.WriteLine($"        {paramName}: {decodeExpr}{separator}");
            }
        }

        // Skip tagged fields for non-compact enums.
        if (!enumDef.IsCompact)
        {
            code.WriteLine("    decoder.SkipTagged();");
        }

        code.WriteLine("    return result;");
        code.WriteLine("}");

        return code;
    }

    private string BuildParameterList(ImmutableList<Field> fields, string currentNamespace)
    {
        return string.Join(", ", fields.Select(f =>
        {
            string typeString = FieldTypeString(f.Type, currentNamespace);
            string paramName = f.EntityInfo.EscapedName;
            return $"{typeString} {paramName}";
        }));
    }
}
