// Copyright (c) ZeroC, Inc.


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
}
