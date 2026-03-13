// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

/// <summary>
/// Represents the information for an entity defined in Slice.
/// </summary>
public record class EntityInfo
{
    /// <summary>
    /// Gets the identifier of the entity.
    /// </summary>
    public required string Identifier { get; init; }

    /// <summary>
    /// Gets the attributes associated with the entity.
    /// </summary>
    public required ImmutableList<Attribute> Attributes { get; init; }

    /// <summary>
    /// Gets the module that contains this entity.
    /// </summary>
    public required Module Module { get; init; }

    /// <summary>
    /// Gets the fully scoped Slice identifier (e.g. "MyModule::MyType").
    /// </summary>
    public string ScopedSliceId => string.IsNullOrEmpty(Module.Identifier)
        ? Identifier
        : $"{Module.Identifier}::{Identifier}";
}
