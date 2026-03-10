// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;
using ZeroC.Slice.Compiler;

namespace IceRpc.SlicecGen;

/// <summary>Utilities for working with Slice fields in code generation.</summary>
internal static class FieldHelpers
{
    /// <summary>Returns fields sorted: non-tagged in original order, then tagged sorted by tag value.</summary>
    internal static IReadOnlyList<Field> GetSortedFields(IList<Field> fields)
    {
        var nonTagged = fields.Where(f => !f.Tag.HasValue).ToList();
        var tagged = fields.Where(f => f.Tag.HasValue).OrderBy(f => f.Tag!.Value).ToList();
        nonTagged.AddRange(tagged);
        return nonTagged;
    }

    /// <summary>Counts non-tagged optional fields (for Slice2 bit sequence sizing).</summary>
    internal static int GetBitSequenceSize(IList<Field> fields) =>
        fields.Count(f => !f.Tag.HasValue && f.DataType.IsOptional);

    /// <summary>Checks if a field should have the 'required' keyword (non-optional reference type).</summary>
    internal static bool IsRequired(Field field, SliceFile file, TypeRegistry registry) =>
        !field.DataType.IsOptional && !TypeResolver.IsValueType(field.DataType, file, registry);

    /// <summary>Checks if a field is tagged.</summary>
    internal static bool IsTagged(Field field) => field.Tag.HasValue;

    /// <summary>Generates a property declaration for a field.</summary>
    internal static CodeBlock FieldDeclaration(
        Field field,
        SliceFile file,
        string currentNamespace,
        string accessModifier,
        bool parentReadonly,
        TypeRegistry registry)
    {
        var code = new CodeBlock();

        // cs::attribute
        foreach (var attr in field.EntityInfo.Attributes.Where(a => a.Directive == "cs::attribute"))
        {
            code.WriteLine($"[{attr.Args[0]}]");
        }

        string typeString = TypeResolver.FieldTypeString(field.DataType, file, currentNamespace, registry);
        string fieldName = CsNaming.FieldName(field);
        string required = IsRequired(field, file, registry) ? "required " : "";
        bool fieldReadonly = CsNaming.HasAttribute(field.EntityInfo.Attributes, "cs::readonly");
        string accessor = (parentReadonly || fieldReadonly) ? "{ get; init; }" : "{ get; set; }";

        code.WriteLine($"{accessModifier} {required}{typeString} {fieldName} {accessor}");

        return code;
    }
}
