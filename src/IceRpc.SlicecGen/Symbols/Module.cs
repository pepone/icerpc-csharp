// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a module defined in Slice. 
/// </summary>
public record class Module
{
    /// <summary>
    /// Gets the identifier for this module.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Gets the list of attributes for this module.
    /// </summary>
    public required ImmutableList<Attribute> Attributes { get; init; }
}
