// Copyright (c) ZeroC, Inc.

using System.Text.Json;

namespace ZeroC.Slice.Codec.Conformance;

/// <summary>A Slice type the executable can encode and decode, with its JSON notation.</summary>
internal interface ISliceType
{
    /// <summary>The Slice type name, for example <c>Conformance::Point</c>.</summary>
    string Name { get; }

    object? FromJson(JsonElement json);

    void Encode(ref SliceEncoder encoder, object? value);

    object? Decode(ref SliceDecoder decoder);
}

/// <summary>Adapts the generated code of <typeparamref name="T" /> to <see cref="ISliceType" />.</summary>
internal sealed class SliceType<T>(string name, EncodeAction<T> encode, DecodeFunc<T> decode) : ISliceType
{
    public string Name => name;

    public object? FromJson(JsonElement json) => JsonValues.Convert(json, typeof(T));

    public void Encode(ref SliceEncoder encoder, object? value) => encode(ref encoder, (T)value!);

    public object? Decode(ref SliceDecoder decoder) => decode(ref decoder);
}
