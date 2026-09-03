// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Codec;

namespace Conformance;

/// <summary>The C# mapping of the custom type <c>Conformance::Fraction</c>.</summary>
public readonly record struct Fraction(long Numerator, ulong Denominator)
{
    public override string ToString() => $"{Numerator}/{Denominator}";
}

/// <summary>Encodes a <see cref="Fraction" /> as a varint62 numerator followed by a varuint62 denominator.</summary>
public static class FractionSliceEncoderExtensions
{
    public static void EncodeFraction(this ref SliceEncoder encoder, Fraction value)
    {
        encoder.EncodeVarInt62(value.Numerator);
        encoder.EncodeVarUInt62(value.Denominator);
    }
}

/// <summary>Decodes a <see cref="Fraction" /> encoded by <see cref="FractionSliceEncoderExtensions" />.</summary>
public static class FractionSliceDecoderExtensions
{
    public static Fraction DecodeFraction(this ref SliceDecoder decoder) =>
        new(decoder.DecodeVarInt62(), decoder.DecodeVarUInt62());
}
