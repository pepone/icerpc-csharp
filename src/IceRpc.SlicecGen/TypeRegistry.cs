// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Compiler;

namespace IceRpc.SlicecGen;

/// <summary>A registry of all named Slice types across all source and reference files, keyed by their fully-scoped
/// TypeId (e.g., "Module1::Module2::TypeName").</summary>
internal sealed class TypeRegistry
{
    private readonly Dictionary<string, Symbol> _symbols;

    /// <summary>Builds a TypeRegistry from all decoded files (source + reference).</summary>
    internal TypeRegistry(IEnumerable<SliceFile> allFiles)
    {
        _symbols = new(StringComparer.Ordinal);

        foreach (SliceFile file in allFiles)
        {
            string moduleScope = file.ModuleDeclaration.Identifier;

            foreach (Symbol symbol in file.Contents)
            {
                if (GetNamedIdentifier(symbol) is string id)
                {
                    string key = string.IsNullOrEmpty(moduleScope) ? id : $"{moduleScope}::{id}";
                    _symbols.TryAdd(key, symbol);
                }
            }
        }
    }

    /// <summary>Looks up a named symbol by its fully-scoped TypeId.</summary>
    internal Symbol? FindSymbol(string typeId) => _symbols.GetValueOrDefault(typeId);

    private static string? GetNamedIdentifier(Symbol symbol) => symbol switch
    {
        Symbol.Struct s => s.V.EntityInfo.Identifier,
        Symbol.Enum e => e.V.EntityInfo.Identifier,
        Symbol.Interface i => i.V.EntityInfo.Identifier,
        Symbol.CustomType c => c.V.EntityInfo.Identifier,
        Symbol.TypeAlias t => t.V.EntityInfo.Identifier,
        _ => null,
    };
}
