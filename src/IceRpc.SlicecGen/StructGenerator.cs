// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace IceRpc.SlicecGen;

/// <summary>Generates C# record structs from Slice struct definitions.</summary>
internal sealed class StructGenerator : Generator
{
    internal StructGenerator(ImmutableList<SliceFile> symbolFiles)
        : base(symbolFiles)
    {
    }

    internal CodeBlock Generate(Struct structDef)
    {
        string escapedIdentifier = structDef.EntityInfo.EscapedName;
        string currentNamespace = structDef.EntityInfo.Namespace;
        string accessModifier = AccessModifier(structDef.EntityInfo);
        bool isReadonly = structDef.EntityInfo.Attributes.HasAttribute(Attribute.CsReadonly);

        // Build the declaration prefix.
        string declaration = isReadonly
            ? $"{accessModifier} readonly partial record struct"
            : $"{accessModifier} partial record struct";

        var builder = new ContainerBuilder(declaration, escapedIdentifier);

        // Add doc comments.
        // TODO: format doc comment from structDef.EntityInfo.Comment

        string scopedId = structDef.EntityInfo.ScopedSliceId;
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this record struct from the Slice struct <c>{scopedId}</c>.");

        // Add property declarations (in original order).
        CodeBlock fieldDeclarations = CodeBlock.FromBlocks(
            structDef.Fields.Select(
                f => FieldDeclaration(f, currentNamespace, accessModifier, isReadonly)));
        builder.AddBlock(fieldDeclarations);

        // Add main constructor.
        builder.AddBlock(GenerateMainConstructor(structDef, currentNamespace, escapedIdentifier, accessModifier));

        // Add decode constructor.
        builder.AddBlock(GenerateDecodeConstructor(structDef, currentNamespace, escapedIdentifier, accessModifier));

        // Add encode method.
        builder.AddBlock(GenerateEncodeMethod(structDef, currentNamespace, accessModifier));

        return builder.Build();
    }

    private CodeBlock GenerateMainConstructor(
        Struct structDef,
        string currentNamespace,
        string escapedIdentifier,
        string accessModifier)
    {
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

        var ctor = new FunctionBuilder(accessModifier, "", escapedIdentifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment("summary", @$"Constructs a new instance of <see cref=""{escapedIdentifier}"" />.");

        foreach (Field field in structDef.Fields)
        {
            string typeString = FieldTypeString(field.Type, currentNamespace);
            string paramName = field.ParameterName;
            ctor.AddParameter(typeString, paramName);
        }

        var body = new CodeBlock();
        foreach (Field field in structDef.Fields)
        {
            body.WriteLine($"this.{field.FieldName} = {field.ParameterName};");
        }
        ctor.SetBody(body);

        return ctor.Build();
    }

    private CodeBlock GenerateDecodeConstructor(
        Struct structDef,
        string currentNamespace,
        string escapedIdentifier,
        string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = GetSortedFields(structDef.Fields);
        bool hasRequiredField = structDef.Fields.Any(f => f.IsRequired);

        var ctor = new FunctionBuilder(accessModifier, "", escapedIdentifier, FunctionType.BlockBody);

        if (hasRequiredField)
        {
            ctor.AddSetsRequiredMembersAttribute();
        }

        ctor.AddComment(
            "summary",
            @$"Constructs a new instance of <see cref=""{escapedIdentifier}"" /> and decodes its fields from a Slice decoder.");
        ctor.AddComment("param", "name", "decoder", "The Slice decoder.");
        ctor.AddParameter("ref SliceDecoder", "decoder");

        var body = new CodeBlock();

        int bitSequenceSize = GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceReader = decoder.GetBitSequenceReader({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string fieldName = field.FieldName;
            string decodeExpr = GetFieldDecodeExpression(field, currentNamespace);
            body.WriteLine($"this.{fieldName} = {decodeExpr};");
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("decoder.SkipTagged();");
        }

        ctor.SetBody(body);
        return ctor.Build();
    }

    private CodeBlock GenerateEncodeMethod(
        Struct structDef,
        string currentNamespace,
        string accessModifier)
    {
        IReadOnlyList<Field> sortedFields = GetSortedFields(structDef.Fields);

        var method = new FunctionBuilder(
            $"{accessModifier} readonly",
            "void",
            "Encode",
            FunctionType.BlockBody);

        method.AddComment("summary", "Encodes the fields of this struct with a Slice encoder.");
        method.AddComment("param", "name", "encoder", "The Slice encoder.");
        method.AddParameter("ref SliceEncoder", "encoder");

        var body = new CodeBlock();

        int bitSequenceSize = GetBitSequenceSize(structDef.Fields);
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            if (field.IsTagged)
            {
                body.WriteLine(EncodeTaggedField(field, currentNamespace));
            }
            else if (field.Type.IsOptional)
            {
                // Non-tagged optional: write bit and encode conditionally.
                string fieldName = field.FieldName;
                string param = $"this.{fieldName}";
                bool isValueType = field.Type.IsValueType;

                body.WriteLine($"bitSequenceWriter.Write({param} != null);");
                string valueParam = isValueType ? $"{param}.Value" : param;
                string encodeExpr = EncodeExpression(field.Type, currentNamespace, valueParam);
                body.WriteLine($"if ({param} != null)");
                body.WriteLine("{");
                body.WriteLine($"    {encodeExpr}");
                body.WriteLine("}");
            }
            else
            {
                body.WriteLine(EncodeField(field, currentNamespace));
            }
        }

        if (!structDef.IsCompact)
        {
            body.WriteLine("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        }

        method.SetBody(body);
        return method.Build();
    }
}
