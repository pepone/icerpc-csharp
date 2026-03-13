// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Symbols;
using Attribute = ZeroC.Slice.Symbols.Attribute;

namespace ZeroC.Slice.Generator;

/// <summary>Extension methods for <see cref="EntityInfo"/> naming helpers.</summary>
internal static class EntityInfoExtensions
{
    private static readonly HashSet<string> _keywords = new(
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte",
        "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "extern", "false", "finally", "fixed", "float",
        "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    ]);

    /// <summary>Escapes a C# keyword by prepending '@'.</summary>
    internal static string EscapeKeyword(string identifier) =>
        _keywords.Contains(identifier) ? $"@{identifier}" : identifier;

    extension(EntityInfo entity)
    {
        /// <summary>Gets the C# namespace for this entity (respects cs::namespace attribute on the module).</summary>
        internal string Namespace
        {
            get
            {
                Module module = entity.Module;
                if (module.Attributes.FindAttribute(Attribute.CsNamespace) is { } attr)
                {
                    return attr.Args[0];
                }
                string[] segments = module.Identifier.Split("::");
                return string.Join(".", segments.Select(s => EscapeKeyword(s.ToPascalCase())));
            }
        }

        /// <summary>Gets the escaped C# identifier (checks cs::identifier attribute, applies PascalCase, escapes
        /// keywords).</summary>
        internal string EscapedName
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(Attribute.CsIdentifier);
                string name = csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToPascalCase();
                return EscapeKeyword(name);
            }
        }

        /// <summary>Gets the camelCase parameter name (checks cs::identifier attribute, escapes keywords).</summary>
        internal string ParameterName
        {
            get
            {
                Attribute? csIdentifier = entity.Attributes.FindAttribute(Attribute.CsIdentifier);
                string name = csIdentifier is { } attr ? attr.Args[0] : entity.Identifier.ToCamelCase();
                return EscapeKeyword(name);
            }
        }
    }
}
