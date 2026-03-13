// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a type alias defined in Slice.
/// </summary>
public record class TypeAlias : Symbol
{
    /// <summary>
    /// Gets the information for the entity associated with this type alias.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the underlying type of this type alias.
    /// </summary>
    public required TypeRef UnderlyingType { get; init; }
}
