// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using System.Globalization;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace IceRpc.SlicecGen;

/// <summary>Generates C# enums and extension classes from Slice enum definitions.</summary>
internal sealed class EnumWithUnderlyingGenerator : Generator
{
    internal EnumWithUnderlyingGenerator(ImmutableList<SliceFile> symbolFiles)
        : base(symbolFiles)
    {
    }

    internal static CodeBlock Generate(EnumWithUnderlying enumDef)
    {
        string escapedIdentifier = enumDef.EntityInfo.EscapedName;
        string accessModifier = AccessModifier(enumDef.EntityInfo);

        return CodeBlock.FromBlocks(
        [
            GenerateEnumDeclaration(enumDef, escapedIdentifier, accessModifier),
            GenerateEnumUnderlyingExtensions(enumDef, escapedIdentifier, accessModifier),
            GenerateEnumEncoderExtensions(enumDef, escapedIdentifier, accessModifier),
            GenerateEnumDecoderExtensions(enumDef, escapedIdentifier, accessModifier),
        ]);
    }

    private static CodeBlock GenerateEnumDeclaration(
        EnumWithUnderlying enumDef,
        string escapedIdentifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder($"{accessModifier} enum", escapedIdentifier);

        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this enum from the Slice enum <c>{scopedId}</c>.");

        // cs::attribute
        foreach (ZeroC.Slice.Symbols.Attribute attr in enumDef.EntityInfo.Attributes.CsAttributes())
        {
            builder.AddAttribute(attr.Args[0]);
        }

        // [System.Flags] for unchecked enums.
        if (enumDef.IsUnchecked)
        {
            builder.AddAttribute("System.Flags");
        }

        builder.AddBase(csType);

        // Add enumerator declarations.
        builder.AddBlock(CodeBlock.FromBlocks(
            enumDef.Enumerators.Select(GenerateEnumeratorDeclaration)));

        return builder.Build();
    }

    private static CodeBlock GenerateEnumeratorDeclaration(EnumWithUnderlying.Enumerator enumerator)
    {
        var code = new CodeBlock();

        foreach (var attr in enumerator.EntityInfo.Attributes.CsAttributes())
        {
            code.WriteLine($"[{attr.Args[0]}]");
        }

        string name = enumerator.EntityInfo.EscapedName;
        string value = EnumeratorValue(enumerator);
        code.WriteLine($"{name} = {value},");
        return code;
    }

    private static CodeBlock GenerateEnumUnderlyingExtensions(
        EnumWithUnderlying enumDef,
        string escapedIdentifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.EntityInfo.ScopedSliceId;
        string article = GetArticle(csType);

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{escapedIdentifier}{csTypePascal}Extensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for creating {GetArticle(escapedIdentifier)} <see cref=""{escapedIdentifier}"" /> from {article} <see langword=""{csType}"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum <c>{scopedId}</c>.");

        bool useSet = NeedsHashSetValidation(enumDef);

        if (useSet)
        {
            string values = string.Join(", ", enumDef.Enumerators.Select(EnumeratorValue));
            var hashSetBlock = new CodeBlock();
            hashSetBlock.WriteLine(
                @$"private static readonly global::System.Collections.Generic.HashSet<{csType}> _enumeratorValues =
    new global::System.Collections.Generic.HashSet<{csType}> {{ {values} }};");
            builder.AddBlock(hashSetBlock);
        }

        // As{EnumName} method.
        var method = new FunctionBuilder(
            $"{accessModifier} static",
            escapedIdentifier,
            $"As{escapedIdentifier}",
            FunctionType.ExpressionBody);

        method.AddParameter($"this {csType}", "value", null, "The value being converted.");
        method.AddComment(
            "summary",
            @$"Converts a <see langword=""{csType}"" /> into the corresponding <see cref=""{escapedIdentifier}"" />
enumerator.");
        method.AddComment("returns", "The enumerator.");

        if (enumDef.IsUnchecked || enumDef.Enumerators.Count == 0)
        {
            method.SetBody($"({escapedIdentifier})value");
        }
        else
        {
            string checkExpr;
            if (useSet)
            {
                checkExpr = "_enumeratorValues.Contains(value)";
            }
            else
            {
                string minValue = enumDef.Enumerators.Select(SignedValue).Min().ToString(CultureInfo.InvariantCulture);
                string maxValue = enumDef.Enumerators.Select(SignedValue).Max().ToString(CultureInfo.InvariantCulture);
                checkExpr = $"value is >= {minValue} and <= {maxValue}";
            }

            method.SetBody(
                @$"{checkExpr} ?
({escapedIdentifier})value :
throw new global::System.IO.InvalidDataException($""Invalid enumerator value '{{value}}' for {escapedIdentifier}."")");

            method.AddComment(
                "exception",
                "cref",
                "global::System.IO.InvalidDataException",
                "Thrown when the value does not correspond to one of the enumerators.");
        }

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private static CodeBlock GenerateEnumEncoderExtensions(
        EnumWithUnderlying enumDef,
        string escapedIdentifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string suffix = enumDef.Underlying.Suffix;
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{escapedIdentifier}SliceEncoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for encoding a <see cref=""{escapedIdentifier}"" /> using a <see cref=""SliceEncoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum " +
            $"<c>{scopedId}</c>.");

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

        method.SetBody($"encoder.Encode{suffix}(({csType})value)");

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    private static CodeBlock GenerateEnumDecoderExtensions(
        EnumWithUnderlying enumDef,
        string escapedIdentifier,
        string accessModifier)
    {
        string csType = enumDef.Underlying.CsType;
        string suffix = enumDef.Underlying.Suffix;
        string csTypePascal = csType.ToPascalCase();
        string scopedId = enumDef.EntityInfo.ScopedSliceId;

        var builder = new ContainerBuilder(
            $"{accessModifier} static class",
            $"{escapedIdentifier}SliceDecoderExtensions");

        builder.AddComment(
            "summary",
            @$"Provides an extension method for decoding a <see cref=""{escapedIdentifier}"" /> using a <see cref=""SliceDecoder"" />.");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice enum " +
            $"<c>{scopedId}</c>.");

        var method = new FunctionBuilder(
            $"{accessModifier} static",
            escapedIdentifier,
            $"Decode{escapedIdentifier}",
            FunctionType.ExpressionBody);

        method.AddComment("summary", @$"Decodes a <see cref=""{escapedIdentifier}"" /> enum.");
        method.AddParameter("this ref SliceDecoder", "decoder", null, "The Slice decoder.");
        method.AddComment(
            "returns",
            @$"The decoded <see cref=""{escapedIdentifier}"" /> enumerator value.");

        method.SetBody(
            $"{escapedIdentifier}{csTypePascal}Extensions.As{escapedIdentifier}(decoder.Decode{suffix}())");

        builder.AddBlock(method.Build());
        return builder.Build();
    }

    // -- Enum helper methods --

    private static string EnumeratorValue(EnumWithUnderlying.Enumerator e) =>
        e.IsPositive
            ? e.AbsoluteValue.ToString(CultureInfo.InvariantCulture)
            : $"-{e.AbsoluteValue.ToString(CultureInfo.InvariantCulture)}";

    private static long SignedValue(EnumWithUnderlying.Enumerator e) =>
        e.IsPositive ? (long)e.AbsoluteValue : -(long)e.AbsoluteValue;

    private static bool NeedsHashSetValidation(EnumWithUnderlying enumDef)
    {
        if (enumDef.IsUnchecked || enumDef.Enumerators.Count == 0)
        {
            return false;
        }

        var values = enumDef.Enumerators.Select(SignedValue).ToList();
        long min = values.Min();
        long max = values.Max();
        return enumDef.Enumerators.Count < (max - min + 1);
    }

    private static string GetArticle(string word) =>
        word.Length > 0 && "aeiouAEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";
}
