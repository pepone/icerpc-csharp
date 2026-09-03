// Copyright (c) ZeroC, Inc.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ZeroC.Slice.Codec.Conformance;

/// <summary>Compares two values structurally and reports every difference with the path to it.</summary>
/// <remarks>The generated Slice types compare collections by reference, so record equality is not enough to check
/// a decoded value. This comparer walks properties, sequences and dictionaries recursively.</remarks>
internal static class StructuralComparer
{
    public static List<string> Compare(object? expected, object? actual)
    {
        var differences = new List<string>();
        Compare(expected, actual, "value", differences);
        return differences;
    }

    private static void Compare(object? expected, object? actual, string path, List<string> differences)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
            {
                Mismatch(differences, path, expected, actual);
            }
            return;
        }

        switch (expected)
        {
            case float expectedFloat when actual is float actualFloat:
                if (!FloatEquals(expectedFloat, actualFloat))
                {
                    Mismatch(differences, path, expected, actual);
                }
                return;

            case double expectedDouble when actual is double actualDouble:
                if (!DoubleEquals(expectedDouble, actualDouble))
                {
                    Mismatch(differences, path, expected, actual);
                }
                return;

            case ReadOnlyMemory<byte> expectedBytes when actual is ReadOnlyMemory<byte> actualBytes:
                if (!expectedBytes.Span.SequenceEqual(actualBytes.Span))
                {
                    Mismatch(differences, path, expected, actual);
                }
                return;

            case IDictionary expectedDictionary when actual is IDictionary actualDictionary:
                CompareDictionaries(expectedDictionary, actualDictionary, path, differences);
                return;

            case string:
                break;

            case IEnumerable expectedSequence when actual is IEnumerable actualSequence:
                CompareSequences(expectedSequence, actualSequence, path, differences);
                return;
        }

        Type type = expected.GetType();
        if (type != actual.GetType())
        {
            differences.Add(
                $"type mismatch at {path}: expected {TypeName(type)}, actual {TypeName(actual.GetType())}");
            return;
        }

        if (IsAtomic(type))
        {
            if (!expected.Equals(actual))
            {
                Mismatch(differences, path, expected, actual);
            }
            return;
        }

        // A struct, record or class: compare its instance properties and fields.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null ||
                property.Name == "EqualityContract")
            {
                continue;
            }
            Compare(property.GetValue(expected), property.GetValue(actual), Child(path, property.Name), differences);
        }
        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.Name.Contains('<'))
            {
                continue; // compiler-generated backing field
            }
            Compare(field.GetValue(expected), field.GetValue(actual), Child(path, field.Name), differences);
        }
    }

    private static void CompareSequences(IEnumerable expected, IEnumerable actual, string path, List<string> differences)
    {
        List<object?> expectedItems = expected.Cast<object?>().ToList();
        List<object?> actualItems = actual.Cast<object?>().ToList();
        if (expectedItems.Count != actualItems.Count)
        {
            differences.Add(
                $"count mismatch at {path}: expected {expectedItems.Count} element(s), actual {actualItems.Count}");
        }
        for (int i = 0; i < Math.Min(expectedItems.Count, actualItems.Count); ++i)
        {
            Compare(expectedItems[i], actualItems[i], $"{path}[{i}]", differences);
        }
    }

    private static void CompareDictionaries(
        IDictionary expected,
        IDictionary actual,
        string path,
        List<string> differences)
    {
        if (expected.Count != actual.Count)
        {
            differences.Add($"count mismatch at {path}: expected {expected.Count} entries, actual {actual.Count}");
        }
        foreach (DictionaryEntry entry in expected)
        {
            if (!actual.Contains(entry.Key))
            {
                differences.Add($"missing key at {path}: {ValueFormatter.Format(entry.Key)}");
                continue;
            }
            Compare(entry.Value, actual[entry.Key], $"{path}[{ValueFormatter.Format(entry.Key)}]", differences);
        }
        foreach (DictionaryEntry entry in actual)
        {
            if (!expected.Contains(entry.Key))
            {
                differences.Add($"unexpected key at {path}: {ValueFormatter.Format(entry.Key)}");
            }
        }
    }

    private static bool FloatEquals(float expected, float actual) =>
        (float.IsNaN(expected) && float.IsNaN(actual)) ||
        BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);

    private static bool DoubleEquals(double expected, double actual) =>
        (double.IsNaN(expected) && double.IsNaN(actual)) ||
        BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);

    private static bool IsAtomic(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
        type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) || type == typeof(Uri) || type == typeof(Int128) || type == typeof(UInt128);

    private static void Mismatch(List<string> differences, string path, object? expected, object? actual)
    {
        differences.Add($"mismatch at {path}:");
        differences.Add($"  expected: {ValueFormatter.Format(expected)}");
        differences.Add($"  actual:   {ValueFormatter.Format(actual)}");
    }

    private static string Child(string path, string name) =>
        path == "value" ? ToCamelCase(name) : $"{path}.{ToCamelCase(name)}";

    // Report field names as they appear in the Slice definitions.
    private static string ToCamelCase(string name) =>
        name.Length > 0 && char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;

    private static string TypeName(Type type) => type.FullName ?? type.Name;
}

/// <summary>Formats values for diagnostics.</summary>
internal static class ValueFormatter
{
    private const int MaxStringLength = 80;
    private const int MaxBytes = 64;
    private const int MaxElements = 16;

    public static string Format(object? value)
    {
        switch (value)
        {
            case null:
                return "none";
            case string s:
                return FormatString(s);
            case float f:
                return $"{f.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.SingleToUInt32Bits(f):X8})";
            case double d:
                return $"{d.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.DoubleToUInt64Bits(d):X16})";
            case ReadOnlyMemory<byte> bytes:
                return Hex(bytes.Span);
            case byte[] bytes:
                return Hex(bytes);
            case IFormattable formattable when value.GetType().IsPrimitive || value is decimal:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            case DateTime dateTime:
                return $"{dateTime:o} (ticks {dateTime.Ticks})";
            case TimeSpan timeSpan:
                return $"{timeSpan} (ticks {timeSpan.Ticks})";
            case IDictionary dictionary:
                return FormatDictionary(dictionary);
            case IEnumerable enumerable:
                return FormatSequence(enumerable);
            default:
                return value.ToString() ?? "null";
        }
    }

    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder();
        int count = Math.Min(bytes.Length, MaxBytes);
        for (int i = 0; i < count; ++i)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }
            builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        if (bytes.Length > count)
        {
            builder.Append($" ... ({bytes.Length} bytes)");
        }
        return builder.Length == 0 ? "(empty)" : builder.ToString();
    }

    private static string FormatString(string value)
    {
        var builder = new StringBuilder("\"");
        int length = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (length >= MaxStringLength)
            {
                builder.Append("\"...");
                break;
            }
            builder.Append(
                rune.Value switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    < 0x20 => $"\\u{rune.Value:X4}",
                    _ => rune.ToString(),
                });
            ++length;
        }
        if (length < MaxStringLength)
        {
            builder.Append('"');
        }
        builder.Append($" ({Encoding.UTF8.GetByteCount(value)} UTF-8 bytes)");
        return builder.ToString();
    }

    private static string FormatSequence(IEnumerable enumerable)
    {
        List<object?> items = enumerable.Cast<object?>().ToList();
        IEnumerable<string> shown = items.Take(MaxElements).Select(Format);
        string suffix = items.Count > MaxElements ? $", ... ({items.Count} elements)" : "";
        return $"[{string.Join(", ", shown)}{suffix}]";
    }

    private static string FormatDictionary(IDictionary dictionary)
    {
        var entries = new List<string>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entries.Count >= MaxElements)
            {
                entries.Add($"... ({dictionary.Count} entries)");
                break;
            }
            entries.Add($"{Format(entry.Key)}: {Format(entry.Value)}");
        }
        return $"{{{string.Join(", ", entries)}}}";
    }
}
