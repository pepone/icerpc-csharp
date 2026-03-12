// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents an attribute applied to a Slice declaration.
/// </summary>
public record class Attribute
{
    /// <summary>
    /// The attributes directive, e.g. "cs::readonly" for [cs::readonly] attribute.
    /// </summary>
    public required string Directive { get; init; }


    /// <summary>
    /// The arguments for the attribute, if any.
    /// </summary>
    public required ImmutableList<string> Args { get; init; }
}
