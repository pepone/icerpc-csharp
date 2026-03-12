// Copyright (c) ZeroC, Inc.

using IceRpc.SlicecGen;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a field in a Slice definition.
/// Slice fields are used in various contexts, such as structs, enumerations, operation parameters, and return values.
/// </summary>
public record class Field
{
    /// <summary>
    /// Gets the entity info for this field.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the tag for this field, if any.
    /// </summary>
    public required int? Tag { get; init; }

    /// <summary>
    /// Gets the type reference for this field.
    /// </summary>
    public required TypeRef Type { get; init; }

    /// <summary>
    /// Gets a value indicating whether this field is tagged.
    /// </summary>
    public bool IsTagged => Tag.HasValue;

    /// <summary>
    /// Gets a value indicating whether this field should have the 'required' keyword (non-optional reference type).
    /// </summary>
    public bool IsRequired => !Type.IsOptional && !Type.IsValueType;

    /// <summary>
    /// Gets the field property name (PascalCase, keyword-escaped).
    /// </summary>
    public string FieldName => EntityInfo.EscapedName;

    /// <summary>
    /// Gets the field parameter name (camelCase, keyword-escaped).
    /// </summary>
    public string ParameterName => EntityInfo.ParameterName;
}
