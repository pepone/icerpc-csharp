// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a custom type in Slice, where the user defines the mapping to the
/// target language as well as the encoding and decoding methods.
/// </summary>
public record class CustomType : Symbol
{
    /// <summary>
    /// Gets the entity info for this custom type.
    /// </summary>
    public required EntityInfo EntityInfo { get; init; }
}
