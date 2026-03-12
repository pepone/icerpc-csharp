// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents an enumerator in a Slice enumeration.
/// </summary>
public record class Enumerator
{
    /// <summary>
    /// Gets the entity info for this enumerator.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }

    /// <summary>
    /// Gets the absolute value of this enumerator.
    /// </summary>
    public required ulong AbsoluteValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether this enumerator is positive.
    /// </summary>
    public required bool IsPositive { get; init; }

    /// <summary>
    /// Gets the list of fields for this enumerator.
    /// </summary>
    public required ImmutableList<Field> Fields { get; init; }
}
