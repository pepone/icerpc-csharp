// Copyright (c) ZeroC, Inc.

using System.Text;
using ZeroC.Slice.Compiler;
using Attribute = ZeroC.Slice.Compiler.Attribute;

namespace IceRpc.SlicecGen;

/// <summary>Utilities for converting Slice identifiers to C# names.</summary>
internal static class CsNaming
{
    private static readonly HashSet<string> CsKeywords = new(
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte",
        "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "extern", "false", "finally", "fixed", "float",
        "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    ]);

    /// <summary>Escapes a C# keyword by prepending '@'.</summary>
    internal static string EscapeKeyword(string identifier) =>
        CsKeywords.Contains(identifier) ? $"@{identifier}" : identifier;

    /// <summary>Converts a Slice identifier to PascalCase with two-letter acronym handling.</summary>
    internal static string ToPascalCase(string identifier) => CsCase(identifier, upperFirst: true);

    /// <summary>Converts a Slice identifier to camelCase with two-letter acronym handling.</summary>
    internal static string ToCamelCase(string identifier) => CsCase(identifier, upperFirst: false);

    /// <summary>Gets the escaped C# identifier for an entity (checks cs::identifier attribute first).</summary>
    internal static string EscapedIdentifier(EntityInfo entity)
    {
        Attribute? csIdentifier = FindAttribute(entity.Attributes, "cs::identifier");
        string name = csIdentifier is { } attr ? attr.Args[0] : ToPascalCase(entity.Identifier);
        return EscapeKeyword(name);
    }

    /// <summary>Gets the camelCase parameter name for an entity.</summary>
    internal static string ParameterName(EntityInfo entity)
    {
        Attribute? csIdentifier = FindAttribute(entity.Attributes, "cs::identifier");
        string name = csIdentifier is { } attr ? attr.Args[0] : ToCamelCase(entity.Identifier);
        return EscapeKeyword(name);
    }

    /// <summary>Gets the field property name (PascalCase + escape).</summary>
    internal static string FieldName(Field field) => EscapedIdentifier(field.EntityInfo);

    /// <summary>Gets the field parameter name (camelCase + escape).</summary>
    internal static string FieldParameterName(Field field) => ParameterName(field.EntityInfo);

    /// <summary>Converts a Module to a C# namespace string (respects cs::namespace attribute).</summary>
    internal static string AsNamespace(Module module)
    {
        if (module.Identifier is null)
        {
            return "";
        }

        Attribute? csNamespace = module.Attributes is not null
            ? FindAttribute(module.Attributes, "cs::namespace")
            : null;
        if (csNamespace is { } attr)
        {
            return attr.Args[0];
        }

        // Convert "Foo::Bar::Baz" to "Foo.Bar.Baz" with PascalCase on each segment.
        string[] segments = module.Identifier.Split("::");
        return string.Join(".", segments.Select(s => EscapeKeyword(ToPascalCase(s))));
    }

    /// <summary>Qualifies a type name relative to the current namespace.</summary>
    internal static string ScopedIdentifier(string identifier, string identifierNamespace, string currentNamespace) =>
        currentNamespace == identifierNamespace
            ? identifier
            : $"global::{identifierNamespace}.{identifier}";

    /// <summary>Gets the access modifier for an entity ("public" or "internal").</summary>
    internal static string AccessModifier(EntityInfo entity) =>
        HasAttribute(entity.Attributes, "cs::internal") ? "internal" : "public";

    /// <summary>Checks if an attribute list contains a specific directive.</summary>
    internal static bool HasAttribute(IList<Attribute> attributes, string directive) =>
        attributes.Any(a => a.Directive == directive);

    /// <summary>Finds an attribute by directive.</summary>
    internal static Attribute? FindAttribute(IList<Attribute> attributes, string directive)
    {
        foreach (Attribute attr in attributes)
        {
            if (attr.Directive == directive)
            {
                return attr;
            }
        }
        return null;
    }

    /// <summary>Port of cs_util.rs:cs_case — converts identifiers with two-letter acronym preservation.</summary>
    private static string CsCase(string original, bool upperFirst)
    {
        // Split the identifier into words on '_' and uppercase boundaries.
        List<string> words = SplitWords(original);
        if (words.Count == 0)
        {
            return original;
        }

        // Convert to PascalCase first.
        var sb = new StringBuilder();
        foreach (string word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }
            sb.Append(char.ToUpperInvariant(word[0]));
            sb.Append(word.AsSpan(1).ToString().ToLowerInvariant());
        }

        string converted = sb.ToString();

        // Apply camelCase if needed.
        if (!upperFirst && converted.Length > 0)
        {
            // For camelCase, lowercase the leading uppercase run.
            int uppercaseRun = 0;
            foreach (char c in converted)
            {
                if (char.IsUpper(c))
                {
                    uppercaseRun++;
                }
                else
                {
                    break;
                }
            }

            if (uppercaseRun > 0)
            {
                char[] chars = converted.ToCharArray();
                // Lowercase all leading uppercase chars except the last one if followed by lowercase.
                int toLower = uppercaseRun > 1 && uppercaseRun < chars.Length ? uppercaseRun - 1 : uppercaseRun;
                for (int i = 0; i < toLower; i++)
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
                converted = new string(chars);
            }
        }

        // Now apply the two-letter acronym fixup.
        // Compare converted characters with original characters (sans underscores).
        char[] originalChars = original.Where(c => c != '_').ToArray();
        if (originalChars.Length != converted.Length)
        {
            return converted;
        }

        char[] result = converted.ToCharArray();
        int uppercaseWordLen = 0;

        for (int index = 0; index < originalChars.Length; index++)
        {
            char originalChar = originalChars[index];

            if (char.IsUpper(originalChar))
            {
                uppercaseWordLen++;
            }
            else
            {
                uppercaseWordLen = 0;
            }

            // For camelCase, skip the leading acronym.
            if (!upperFirst && index < uppercaseWordLen)
            {
                continue;
            }

            bool hasNext = index + 1 < originalChars.Length;
            char? nextOriginalChar = hasNext ? originalChars[index + 1] : null;

            // If third uppercase char followed by lowercase: fix previous char.
            if (uppercaseWordLen == 3 && nextOriginalChar.HasValue && char.IsLower(nextOriginalChar.Value))
            {
                int pos = index - 1;
                result[pos] = originalChars[pos];
            }
            // If second uppercase char at end of string: fix current char.
            else if (!hasNext && uppercaseWordLen == 2)
            {
                result[index] = originalChar;
            }
        }

        return new string(result);
    }

    /// <summary>Splits an identifier into words on underscore and uppercase boundaries.</summary>
    private static List<string> SplitWords(string identifier)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];
            if (c == '_')
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (char.IsUpper(c) && current.Length > 0 && !char.IsUpper(current[^1]))
            {
                // Transition from lowercase to uppercase: new word.
                words.Add(current.ToString());
                current.Clear();
                current.Append(c);
            }
            else if (char.IsUpper(c) && current.Length > 1 && char.IsUpper(current[^1])
                && i + 1 < identifier.Length && char.IsLower(identifier[i + 1]))
            {
                // Run of uppercase followed by lowercase: split before current.
                words.Add(current.ToString());
                current.Clear();
                current.Append(c);
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
