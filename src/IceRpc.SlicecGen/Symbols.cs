// Copyright (c) ZeroC, Inc.

#pragma warning disable CS1591 // Missing XML Comment

using System.Collections.Immutable;

namespace ZeroC.Slice.Symbols;

public record class Attribute
{
    public required string Directive { get; init; }
    public required ImmutableList<string> Args { get; init; }
}

public record class TypeRef
{
    public required Symbol Symbol { get; init; }
    public required bool IsOptional { get; init; }
    public required ImmutableList<Attribute> Attributes { get; init; }
}

public record class EntityInfo
{
    public required string Identifier { get; init; }
    public required ImmutableList<Attribute> Attributes { get; init; }
}

public record class Module
{
    public required string Identifier { get; init; }
    public required ImmutableList<Attribute> Attributes { get; init; }
}

public record class Field
{
    public required EntityInfo EntityInfo { get; init; }
    public required int? Tag { get; init; }
    public required TypeRef Type { get; init; }
}

/// <summary>Base for all types that can be referenced by a <see cref="TypeRef"/>.</summary>
public abstract record class Symbol;

public enum BuiltinKind
{
    Bool,
    Int8,
    UInt8,
    Int16,
    UInt16,
    Int32,
    UInt32,
    VarInt32,
    VarUInt32,
    Int64,
    UInt64,
    VarInt62,
    VarUInt62,
    Float32,
    Float64,
    String,
}

public record class Builtin : Symbol
{
    public required BuiltinKind Kind { get; init; }
}

public record class Struct : Symbol
{
    public required EntityInfo EntityInfo { get; init; }
    public required bool IsCompact { get; init; }
    public required ImmutableList<Field> Fields { get; init; }
}

public record class Enum : Symbol
{
    public required EntityInfo EntityInfo { get; init; }
    public required bool IsCompact { get; init; }
    public required bool IsUnchecked { get; init; }
    public required Builtin? Underlying { get; init; }
    public required ImmutableList<Enumerator> Enumerators { get; init; }
}

public record class Enumerator
{
    public required EntityInfo EntityInfo { get; init; }
    public required ulong AbsoluteValue { get; init; }
    public required bool IsPositive { get; init; }
    public required ImmutableList<Field> Fields { get; init; }
}

public record class Interface : Symbol
{
    public required EntityInfo EntityInfo { get; init; }
    public required ImmutableList<Interface> Bases { get; init; }
    public required ImmutableList<Operation> Operations { get; init; }
}

public record class Operation
{
    public required EntityInfo EntityInfo { get; init; }
    public required bool IsIdempotent { get; init; }
    public required ImmutableList<Field> Parameters { get; init; }
    public required bool HasStreamedParameter { get; init; }
    public required ImmutableList<Field> ReturnType { get; init; }
    public required bool HasStreamedReturn { get; init; }
}

public record class CustomType : Symbol
{
    public required EntityInfo EntityInfo { get; init; }
}

public record class TypeAlias : Symbol
{
    public required EntityInfo EntityInfo { get; init; }
    public required TypeRef UnderlyingType { get; init; }
}

public record class SequenceType : Symbol
{
    public required TypeRef ElementType { get; init; }
}

public record class DictionaryType : Symbol
{
    public required TypeRef KeyType { get; init; }
    public required TypeRef ValueType { get; init; }
}

public record class ResultType : Symbol
{
    public required TypeRef SuccessType { get; init; }
    public required TypeRef FailureType { get; init; }
}

public record class SliceFile
{
    public required string Path { get; init; }
    public required Module Module { get; init; }
    public required ImmutableList<Attribute> Attributes { get; init; }
    public required ImmutableList<Symbol> Contents { get; init; }
}
