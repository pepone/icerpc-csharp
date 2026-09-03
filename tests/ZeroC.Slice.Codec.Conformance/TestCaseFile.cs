// Copyright (c) ZeroC, Inc.

using System.Text.Json;

namespace ZeroC.Slice.Codec.Conformance;

/// <summary>A test-case file of the conformance suite (test-cases/*.json): a Slice type, a value in the suite's JSON
/// notation and the golden bytes.</summary>
internal sealed class TestCaseFile
{
    /// <summary>The test-case identifier: the file name without its extension.</summary>
    public string Id { get; }

    /// <summary>The Slice type the encoder encodes.</summary>
    public string Type { get; }

    /// <summary>The Slice type the decoder decodes (the same as <see cref="Type" /> unless the file says
    /// otherwise).</summary>
    public string DecodeType { get; }

    /// <summary>The value the encoder encodes.</summary>
    public JsonElement Value { get; }

    /// <summary>The value the decoder must produce.</summary>
    public JsonElement DecodedValue { get; }

    public static TestCaseFile Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return new TestCaseFile(Path.GetFileNameWithoutExtension(path), document.RootElement.Clone());
    }

    private TestCaseFile(string id, JsonElement root)
    {
        Id = id;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The test case must be a JSON object.");
        }
        Type = GetString(root, "type");
        Value = root.TryGetProperty("value", out JsonElement value) ?
            value :
            throw new FormatException("The test case has no 'value'.");
        DecodeType = root.TryGetProperty("decodeType", out _) ? GetString(root, "decodeType") : Type;
        DecodedValue = root.TryGetProperty("decodedValue", out JsonElement decodedValue) ? decodedValue : Value;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String ?
            property.GetString()! :
            throw new FormatException($"The test case has no string '{name}'.");
}
