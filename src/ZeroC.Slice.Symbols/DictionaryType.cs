// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents a dictionary type in Slice, where the user defines the key and value types.
/// </summary>
public record class DictionaryType : Symbol
{
    /// <summary>
    /// Gets the type of the keys in the dictionary.
    /// </summary>
    public required TypeRef KeyType { get; init; }

    /// <summary>
    /// Gets the type of the values in the dictionary.
    /// </summary>
    public required TypeRef ValueType { get; init; }
}
