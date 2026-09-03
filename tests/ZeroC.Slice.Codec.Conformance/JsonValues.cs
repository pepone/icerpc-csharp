// Copyright (c) ZeroC, Inc.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ZeroC.Slice.Codec.Conformance;

/// <summary>Converts values written in the conformance suite's JSON notation into instances of the generated
/// types, using reflection so that new Slice types need no conversion code.</summary>
/// <remarks>The notation: integers and finite floats are JSON numbers ("NaN", "Infinity" and "-Infinity" are
/// strings); an unset optional is null; a sequence is an array; a dictionary is an object whose keys are the
/// formatted key values; a struct is an object with the Slice field names; a basic enum is the enumerator name (or a
/// number); a variant is <c>{ "Name": { fields } }</c> or just <c>"Name"</c> when it has no fields; a
/// <c>Result</c> is <c>{ "Success": value }</c> or <c>{ "Failure": value }</c>; raw bytes, ticks, URIs and UUIDs
/// are strings or numbers as documented in the suite's README.</remarks>
internal static class JsonValues
{
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // "NaN" in the JSON notation is the canonical quiet NaN (sign bit clear, quiet bit set, zero payload), not
    // float.NaN/double.NaN, whose sign bit is set on .NET.
    private static readonly float CanonicalSingleNaN = BitConverter.Int32BitsToSingle(0x7FC00000);
    private static readonly double CanonicalDoubleNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000);

    private static readonly HashSet<Type> _sequenceTypes =
    [
        typeof(IList<>),
        typeof(IReadOnlyList<>),
        typeof(ICollection<>),
        typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>),
        typeof(List<>),
    ];

    private static readonly HashSet<Type> _dictionaryTypes =
    [
        typeof(IDictionary<,>),
        typeof(IReadOnlyDictionary<,>),
        typeof(Dictionary<,>),
    ];

    public static object? Convert(JsonElement json, Type type)
    {
        if (Nullable.GetUnderlyingType(type) is Type underlying)
        {
            return json.ValueKind == JsonValueKind.Null ? null : Convert(json, underlying);
        }
        if (json.ValueKind == JsonValueKind.Null)
        {
            return type.IsValueType ?
                throw new FormatException($"null is not a valid {TypeName(type)}.") :
                null;
        }

        if (type == typeof(bool))
        {
            return json.GetBoolean();
        }
        if (type == typeof(sbyte))
        {
            return checked((sbyte)ReadInt64(json));
        }
        if (type == typeof(byte))
        {
            return checked((byte)ReadUInt64(json));
        }
        if (type == typeof(short))
        {
            return checked((short)ReadInt64(json));
        }
        if (type == typeof(ushort))
        {
            return checked((ushort)ReadUInt64(json));
        }
        if (type == typeof(int))
        {
            return checked((int)ReadInt64(json));
        }
        if (type == typeof(uint))
        {
            return checked((uint)ReadUInt64(json));
        }
        if (type == typeof(long))
        {
            return ReadInt64(json);
        }
        if (type == typeof(ulong))
        {
            return ReadUInt64(json);
        }
        if (type == typeof(float))
        {
            return ReadSingle(json);
        }
        if (type == typeof(double))
        {
            return ReadDouble(json);
        }
        if (type == typeof(string))
        {
            return ReadString(json);
        }
        if (type == typeof(TimeSpan))
        {
            return TimeSpan.FromTicks(ReadInt64(json));
        }
        if (type == typeof(DateTime))
        {
            return new DateTime(ReadInt64(json), DateTimeKind.Utc);
        }
        if (type == typeof(Uri))
        {
            return new Uri(ReadString(json));
        }
        if (type == typeof(Guid))
        {
            return Guid.Parse(ReadString(json), CultureInfo.InvariantCulture);
        }
        if (type == typeof(ReadOnlyMemory<byte>))
        {
            return new ReadOnlyMemory<byte>(ReadHex(json));
        }
        if (type.IsEnum)
        {
            return json.ValueKind == JsonValueKind.String ?
                Enum.Parse(type, json.GetString()!) :
                Enum.ToObject(type, Convert(json, Enum.GetUnderlyingType(type))!);
        }
        if (TryGetElementType(type, out Type? element))
        {
            return ConvertSequence(json, element);
        }
        if (TryGetDictionaryTypes(type, out Type? key, out Type? value))
        {
            return ConvertDictionary(json, key, value);
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<,>))
        {
            return ConvertResult(json, type);
        }
        if (type.IsAbstract && type.IsClass)
        {
            return ConvertVariant(json, type);
        }
        return ConvertStruct(json, type);
    }

    private static object ConvertSequence(JsonElement json, Type element)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Expected an array for a sequence of {TypeName(element)}.");
        }
        var array = Array.CreateInstance(element, json.GetArrayLength());
        int i = 0;
        foreach (JsonElement item in json.EnumerateArray())
        {
            array.SetValue(Convert(item, element), i++);
        }
        return array;
    }

    private static object ConvertDictionary(JsonElement json, Type key, Type value)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Expected an object for a dictionary with {TypeName(key)} keys.");
        }
        var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(key, value))!;
        foreach (JsonProperty property in json.EnumerateObject())
        {
            dictionary[ConvertKey(property.Name, key)] = Convert(property.Value, value);
        }
        return dictionary;
    }

    private static object ConvertKey(string text, Type type)
    {
        if (type == typeof(string))
        {
            return text;
        }
        if (type.IsEnum)
        {
            return Enum.Parse(type, text);
        }
        using var document = JsonDocument.Parse(text);
        return Convert(document.RootElement, type) ??
            throw new FormatException($"'{text}' is not a valid {TypeName(type)} key.");
    }

    private static object ConvertResult(JsonElement json, Type type)
    {
        (string name, JsonElement payload) = SingleProperty(json, "a Result");
        if (name is not ("Success" or "Failure"))
        {
            throw new FormatException($"'{name}' is not a Result case (expected 'Success' or 'Failure').");
        }
        Type caseType = typeof(Result<,>).GetNestedType(name)!.MakeGenericType(type.GetGenericArguments());
        ConstructorInfo constructor = caseType.GetConstructors(AllInstance).Single(
            c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType != caseType);
        return constructor.Invoke([Convert(payload, constructor.GetParameters()[0].ParameterType)]);
    }

    private static object ConvertVariant(JsonElement json, Type type)
    {
        string name;
        JsonElement? fields;
        if (json.ValueKind == JsonValueKind.String)
        {
            name = json.GetString()!;
            fields = null;
        }
        else
        {
            (name, JsonElement payload) = SingleProperty(json, $"a {TypeName(type)} variant");
            fields = payload.ValueKind == JsonValueKind.Null ? null : payload;
        }
        Type? variant = type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
        if (variant is null || !variant.IsSubclassOf(type))
        {
            throw new FormatException($"'{name}' is not a variant of {TypeName(type)}.");
        }
        return ConvertStruct(fields, variant);
    }

    /// <summary>Constructs a struct, record or class from a JSON object by calling its constructor with the JSON
    /// fields as arguments.</summary>
    private static object ConvertStruct(JsonElement? json, Type type)
    {
        if (json is JsonElement element && element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Expected an object for a {TypeName(type)}.");
        }

        // Skip the decoding constructor and the copy constructor of records.
        ConstructorInfo? constructor = type
            .GetConstructors(AllInstance)
            .Where(c => !c.GetParameters().Any(
                p => p.ParameterType == typeof(SliceDecoder).MakeByRefType() || p.ParameterType == type))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (json is JsonElement fields)
        {
            foreach (JsonProperty property in fields.EnumerateObject())
            {
                properties[property.Name] = property.Value;
            }
        }

        if (constructor is null)
        {
            if (properties.Count > 0 || !type.IsValueType)
            {
                throw new FormatException($"{TypeName(type)} has no usable constructor.");
            }
            return Activator.CreateInstance(type)!;
        }

        ParameterInfo[] parameters = constructor.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; ++i)
        {
            string parameterName = parameters[i].Name!;
            if (!properties.Remove(parameterName, out JsonElement value))
            {
                throw new FormatException($"Missing field '{parameterName}' for {TypeName(type)}.");
            }
            arguments[i] = Convert(value, parameters[i].ParameterType);
        }
        if (properties.Count > 0)
        {
            throw new FormatException($"Unknown field '{properties.Keys.First()}' for {TypeName(type)}.");
        }
        return constructor.Invoke(arguments);
    }

    private static (string Name, JsonElement Value) SingleProperty(JsonElement json, string what)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Expected an object with a single property for {what}.");
        }
        JsonProperty[] properties = json.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            throw new FormatException($"Expected exactly one property for {what}, found {properties.Length}.");
        }
        return (properties[0].Name, properties[0].Value);
    }

    private static bool TryGetElementType(Type type, out Type element)
    {
        if (type.IsArray)
        {
            element = type.GetElementType()!;
            return true;
        }
        if (type.IsGenericType && _sequenceTypes.Contains(type.GetGenericTypeDefinition()))
        {
            element = type.GetGenericArguments()[0];
            return true;
        }
        element = null!;
        return false;
    }

    private static bool TryGetDictionaryTypes(Type type, out Type key, out Type value)
    {
        if (type.IsGenericType && _dictionaryTypes.Contains(type.GetGenericTypeDefinition()))
        {
            Type[] arguments = type.GetGenericArguments();
            key = arguments[0];
            value = arguments[1];
            return true;
        }
        key = null!;
        value = null!;
        return false;
    }

    private static long ReadInt64(JsonElement json) =>
        json.ValueKind switch
        {
            JsonValueKind.Number => json.GetInt64(),
            JsonValueKind.String => long.Parse(json.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new FormatException($"Expected an integer, found {json.ValueKind}."),
        };

    private static ulong ReadUInt64(JsonElement json) =>
        json.ValueKind switch
        {
            JsonValueKind.Number => json.GetUInt64(),
            JsonValueKind.String => ulong.Parse(json.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new FormatException($"Expected an unsigned integer, found {json.ValueKind}."),
        };

    private static float ReadSingle(JsonElement json) =>
        json.ValueKind switch
        {
            JsonValueKind.Number => json.GetSingle(),
            JsonValueKind.String => json.GetString() switch
            {
                "NaN" => CanonicalSingleNaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                string text => float.Parse(text, CultureInfo.InvariantCulture),
                null => throw new FormatException("Expected a float."),
            },
            _ => throw new FormatException($"Expected a float, found {json.ValueKind}."),
        };

    private static double ReadDouble(JsonElement json) =>
        json.ValueKind switch
        {
            JsonValueKind.Number => json.GetDouble(),
            JsonValueKind.String => json.GetString() switch
            {
                "NaN" => CanonicalDoubleNaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                string text => double.Parse(text, CultureInfo.InvariantCulture),
                null => throw new FormatException("Expected a float."),
            },
            _ => throw new FormatException($"Expected a float, found {json.ValueKind}."),
        };

    private static string ReadString(JsonElement json) =>
        json.ValueKind == JsonValueKind.String ?
            json.GetString()! :
            throw new FormatException($"Expected a string, found {json.ValueKind}.");

    private static byte[] ReadHex(JsonElement json) =>
        System.Convert.FromHexString(ReadString(json).Replace(" ", "", StringComparison.Ordinal));

    private static string TypeName(Type type) => type.Name;
}
