// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a sequence type defined in Slice.
/// </summary>
public record class SequenceType : Symbol
{
    /// <summary>
    /// Gets the type of the elements in the sequence.
    /// </summary>
    public required TypeRef ElementType { get; init; }
}
