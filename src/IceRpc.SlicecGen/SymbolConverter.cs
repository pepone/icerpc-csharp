// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using Symbols = ZeroC.Slice.Symbols;
using Compiler = ZeroC.Slice.Compiler;

namespace IceRpc.SlicecGen;

/// <summary>Converts decoded Slice compiler types (string-based TypeIds) into rich symbol types (direct object
/// references).</summary>
internal sealed class SymbolConverter
{
    private static readonly Dictionary<string, Symbols.Builtin> Builtins = new(StringComparer.Ordinal)
    {
        ["bool"] = new() { Kind = Symbols.BuiltinKind.Bool },
        ["int8"] = new() { Kind = Symbols.BuiltinKind.Int8 },
        ["uint8"] = new() { Kind = Symbols.BuiltinKind.UInt8 },
        ["int16"] = new() { Kind = Symbols.BuiltinKind.Int16 },
        ["uint16"] = new() { Kind = Symbols.BuiltinKind.UInt16 },
        ["int32"] = new() { Kind = Symbols.BuiltinKind.Int32 },
        ["uint32"] = new() { Kind = Symbols.BuiltinKind.UInt32 },
        ["varint32"] = new() { Kind = Symbols.BuiltinKind.VarInt32 },
        ["varuint32"] = new() { Kind = Symbols.BuiltinKind.VarUInt32 },
        ["int64"] = new() { Kind = Symbols.BuiltinKind.Int64 },
        ["uint64"] = new() { Kind = Symbols.BuiltinKind.UInt64 },
        ["varint62"] = new() { Kind = Symbols.BuiltinKind.VarInt62 },
        ["varuint62"] = new() { Kind = Symbols.BuiltinKind.VarUInt62 },
        ["float32"] = new() { Kind = Symbols.BuiltinKind.Float32 },
        ["float64"] = new() { Kind = Symbols.BuiltinKind.Float64 },
        ["string"] = new() { Kind = Symbols.BuiltinKind.String },
    };

    // Index of all named types across all files, keyed by fully-scoped TypeId.
    private readonly Dictionary<string, (Compiler.SliceFile File, Compiler.Symbol Symbol)> _named;

    // Cache of converted named symbols, keyed by fully-scoped TypeId.
    private readonly Dictionary<string, Symbols.Symbol> _cache = new(StringComparer.Ordinal);

    internal SymbolConverter(IEnumerable<Compiler.SliceFile> allFiles)
    {
        _named = new(StringComparer.Ordinal);

        foreach (Compiler.SliceFile file in allFiles)
        {
            string moduleScope = file.ModuleDeclaration.Identifier;

            foreach (Compiler.Symbol symbol in file.Contents)
            {
                if (GetNamedIdentifier(symbol) is string id)
                {
                    string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                    _named.TryAdd(key, (file, symbol));
                }
            }
        }
    }

    /// <summary>Converts source files into rich symbol types with all TypeRefs resolved.</summary>
    internal ImmutableList<Symbols.SliceFile> ConvertFiles(IEnumerable<Compiler.SliceFile> sourceFiles) =>
        sourceFiles.Select(ConvertFile).ToImmutableList();

    private Symbols.SliceFile ConvertFile(Compiler.SliceFile file)
    {
        string moduleScope = file.ModuleDeclaration.Identifier;
        Symbols.Module module = ConvertModule(file.ModuleDeclaration);

        var contents = ImmutableList.CreateBuilder<Symbols.Symbol>();
        for (int i = 0; i < file.Contents.Count; i++)
        {
            Compiler.Symbol raw = file.Contents[i];
            string? id = GetNamedIdentifier(raw);

            if (id is not null)
            {
                // Named type — resolve through cache using its scoped TypeId.
                string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                contents.Add(ResolveNamedType(key));
            }
            else
            {
                // Anonymous type — convert inline (scoped to this file).
                contents.Add(ConvertSymbol(raw, file, module));
            }
        }

        return new()
        {
            Path = file.Path,
            Module = module,
            Attributes = ConvertAttributes(file.Attributes),
            Contents = contents.ToImmutable(),
        };
    }

    /// <summary>Resolves a TypeId to a Symbol, handling primitives, anonymous types, and named types.</summary>
    private Symbols.Symbol ResolveTypeId(string typeId, Compiler.SliceFile currentFile)
    {
        // Builtin.
        if (Builtins.TryGetValue(typeId, out Symbols.Builtin? prim))
        {
            return prim;
        }

        // Anonymous type (numeric index into currentFile.Contents).
        if (int.TryParse(typeId, out int index))
        {
            Symbols.Module module = ConvertModule(currentFile.ModuleDeclaration);
            return ConvertSymbol(currentFile.Contents[index], currentFile, module);
        }

        // Named type.
        return ResolveNamedType(typeId);
    }

    private Symbols.Symbol ResolveNamedType(string typeId)
    {
        if (_cache.TryGetValue(typeId, out Symbols.Symbol? cached))
        {
            return cached;
        }

        (Compiler.SliceFile file, Compiler.Symbol symbol) = _named[typeId];
        Symbols.Module module = ConvertModule(file.ModuleDeclaration);
        Symbols.Symbol converted = ConvertSymbol(symbol, file, module);
        _cache[typeId] = converted;
        return converted;
    }

    private Symbols.Symbol ConvertSymbol(Compiler.Symbol symbol, Compiler.SliceFile file, Symbols.Module module) =>
        symbol switch
    {
        Compiler.Symbol.Struct s => ConvertStruct(s.V, file, module),
        Compiler.Symbol.Enum e => ConvertEnum(e.V, file, module),
        Compiler.Symbol.Interface i => ConvertInterface(i.V, file, module),
        Compiler.Symbol.CustomType c => new Symbols.CustomType
        {
            EntityInfo = ConvertEntityInfo(c.V.EntityInfo, module),
        },
        Compiler.Symbol.TypeAlias t => new Symbols.TypeAlias
        {
            EntityInfo = ConvertEntityInfo(t.V.EntityInfo, module),
            UnderlyingType = ConvertTypeRef(t.V.UnderlyingType, file),
        },
        Compiler.Symbol.SequenceType s => new Symbols.SequenceType
        {
            ElementType = ConvertTypeRef(s.V.ElementType, file),
        },
        Compiler.Symbol.DictionaryType d => new Symbols.DictionaryType
        {
            KeyType = ConvertTypeRef(d.V.KeyType, file),
            ValueType = ConvertTypeRef(d.V.ValueType, file),
        },
        Compiler.Symbol.ResultType r => new Symbols.ResultType
        {
            SuccessType = ConvertTypeRef(r.V.SuccessType, file),
            FailureType = ConvertTypeRef(r.V.FailureType, file),
        },
        _ => throw new InvalidOperationException($"Unknown symbol type: {symbol.GetType().Name}"),
    };

    private Symbols.Struct ConvertStruct(Compiler.Struct raw, Compiler.SliceFile file, Symbols.Module module) => new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        IsCompact = raw.IsCompact,
        Fields = raw.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
    };

    private Symbols.Symbol ConvertEnum(Compiler.Enum raw, Compiler.SliceFile file, Symbols.Module module)
    {
        if (raw.Underlying is string u && Builtins.TryGetValue(u, out var builtin))
        {
            return new Symbols.EnumWithUnderlying
            {
                EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
                IsCompact = raw.IsCompact,
                IsUnchecked = raw.IsUnchecked,
                Underlying = builtin,
                Enumerators = raw.Enumerators.Select(e => new Symbols.EnumWithUnderlying.Enumerator
                {
                    EntityInfo = ConvertEntityInfo(e.EntityInfo, module),
                    AbsoluteValue = e.Value.AbsoluteValue,
                    IsPositive = e.Value.IsPositive,
                }).ToImmutableList(),
            };
        }
        else
        {
            return new Symbols.EnumWithFields
            {
                EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
                IsCompact = raw.IsCompact,
                IsUnchecked = raw.IsUnchecked,
                Enumerators = raw.Enumerators.Select(e => new Symbols.EnumWithFields.Enumerator
                {
                    EntityInfo = ConvertEntityInfo(e.EntityInfo, module),
                    Fields = e.Fields.Select(f => ConvertField(f, file, module)).ToImmutableList(),
                }).ToImmutableList(),
            };
        }
    }

    private Symbols.Interface ConvertInterface(Compiler.Interface raw, Compiler.SliceFile file, Symbols.Module module) =>
        new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        Bases = raw.Bases
            .Select(baseId => ResolveNamedType(baseId))
            .OfType<Symbols.Interface>()
            .ToImmutableList(),
        Operations = raw.Operations.Select(op => ConvertOperation(op, file, module)).ToImmutableList(),
    };

    private Symbols.Operation ConvertOperation(
        Compiler.Operation raw,
        Compiler.SliceFile file,
        Symbols.Module module) => new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        IsIdempotent = raw.IsIdempotent,
        Parameters = raw.Parameters.Select(f => ConvertField(f, file, module)).ToImmutableList(),
        HasStreamedParameter = raw.HasStreamedParameter,
        ReturnType = raw.ReturnType.Select(f => ConvertField(f, file, module)).ToImmutableList(),
        HasStreamedReturn = raw.HasStreamedReturn,
    };

    private Symbols.Field ConvertField(Compiler.Field raw, Compiler.SliceFile file, Symbols.Module module) => new()
    {
        EntityInfo = ConvertEntityInfo(raw.EntityInfo, module),
        Tag = raw.Tag,
        Type = ConvertTypeRef(raw.DataType, file),
    };

    private Symbols.TypeRef ConvertTypeRef(Compiler.TypeRef raw, Compiler.SliceFile file) => new()
    {
        Symbol = ResolveTypeId(raw.TypeId, file),
        IsOptional = raw.IsOptional,
        Attributes = ConvertAttributes(raw.TypeAttributes),
    };

    private static Symbols.EntityInfo ConvertEntityInfo(Compiler.EntityInfo raw, Symbols.Module module) => new()
    {
        Identifier = raw.Identifier,
        Attributes = ConvertAttributes(raw.Attributes),
        Module = module,
    };

    private static Symbols.Module ConvertModule(Compiler.Module raw) => new()
    {
        Identifier = raw.Identifier,
        Attributes = ConvertAttributes(raw.Attributes),
    };

    private static ImmutableList<Symbols.Attribute> ConvertAttributes(IList<Compiler.Attribute> raw) =>
        raw.Select(a => new Symbols.Attribute
        {
            Directive = a.Directive,
            Args = [.. a.Args],
        }).ToImmutableList();

    private static string? GetNamedIdentifier(Compiler.Symbol symbol) => symbol switch
    {
        Compiler.Symbol.Struct s => s.V.EntityInfo.Identifier,
        Compiler.Symbol.Enum e => e.V.EntityInfo.Identifier,
        Compiler.Symbol.Interface i => i.V.EntityInfo.Identifier,
        Compiler.Symbol.CustomType c => c.V.EntityInfo.Identifier,
        Compiler.Symbol.TypeAlias t => t.V.EntityInfo.Identifier,
        _ => null,
    };
}
