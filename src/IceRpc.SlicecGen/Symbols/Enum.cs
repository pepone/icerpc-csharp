// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents an enumeration type defined in Slice.
/// </summary>
public record class Enum : Symbol
{
    /// <summary>
    /// Gets the entity info for this enumeration.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumeration is a compact enumeration.
    /// </summary>
    public required bool IsCompact { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumeration is unchecked.
    /// </summary>
    public required bool IsUnchecked { get; init; }

    /// <summary>
    /// Gets the underlying type of this enumeration, if any.
    /// </summary>
    public required Builtin? Underlying { get; init; }

    /// <summary>
    /// Gets the list of enumerators for this enumeration.
    /// </summary>
    public required ImmutableList<Enumerator> Enumerators { get; init; }
}
