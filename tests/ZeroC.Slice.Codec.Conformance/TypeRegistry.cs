// Copyright (c) ZeroC, Inc.

using Conformance;

namespace ZeroC.Slice.Codec.Conformance;

/// <summary>The Slice types of slice/Conformance.slice, by Slice name.</summary>
internal static class TypeRegistry
{
    private static readonly Dictionary<string, ISliceType> _types = new ISliceType[]
    {
        Type<Bools>("Bools", (ref SliceEncoder e, Bools v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<FixedIntegers>(
            "FixedIntegers",
            (ref SliceEncoder e, FixedIntegers v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<VarIntegers>(
            "VarIntegers",
            (ref SliceEncoder e, VarIntegers v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<VarIntegerLimits>(
            "VarIntegerLimits",
            (ref SliceEncoder e, VarIntegerLimits v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Floats>("Floats", (ref SliceEncoder e, Floats v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<FloatSpecials>(
            "FloatSpecials",
            (ref SliceEncoder e, FloatSpecials v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Strings>("Strings", (ref SliceEncoder e, Strings v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<Sequences>(
            "Sequences",
            (ref SliceEncoder e, Sequences v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<OptionalSequences>(
            "OptionalSequences",
            (ref SliceEncoder e, OptionalSequences v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Dictionaries>(
            "Dictionaries",
            (ref SliceEncoder e, Dictionaries v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<OptionalDictionaries>(
            "OptionalDictionaries",
            (ref SliceEncoder e, OptionalDictionaries v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Point>("Point", (ref SliceEncoder e, Point v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<Empty>("Empty", (ref SliceEncoder e, Empty v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<Person>("Person", (ref SliceEncoder e, Person v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<LineStyle>(
            "LineStyle",
            (ref SliceEncoder e, LineStyle v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Box>("Box", (ref SliceEncoder e, Box v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<Contact>("Contact", (ref SliceEncoder e, Contact v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<ManyOptionals>(
            "ManyOptionals",
            (ref SliceEncoder e, ManyOptionals v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<TaggedContact>(
            "TaggedContact",
            (ref SliceEncoder e, TaggedContact v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<TaggedFields>(
            "TaggedFields",
            (ref SliceEncoder e, TaggedFields v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<TaggedFieldsSubset>(
            "TaggedFieldsSubset",
            (ref SliceEncoder e, TaggedFieldsSubset v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Fruit>("Fruit", (ref SliceEncoder e, Fruit v) => e.EncodeFruit(v), (ref SliceDecoder d) => d.DecodeFruit()),
        Type<Priority>(
            "Priority",
            (ref SliceEncoder e, Priority v) => e.EncodePriority(v),
            (ref SliceDecoder d) => d.DecodePriority()),
        Type<Status>("Status", (ref SliceEncoder e, Status v) => e.EncodeStatus(v), (ref SliceDecoder d) => d.DecodeStatus()),
        Type<Level>("Level", (ref SliceEncoder e, Level v) => e.EncodeLevel(v), (ref SliceDecoder d) => d.DecodeLevel()),
        Type<Permissions>(
            "Permissions",
            (ref SliceEncoder e, Permissions v) => e.EncodePermissions(v),
            (ref SliceDecoder d) => d.DecodePermissions()),
        Type<BasicEnums>(
            "BasicEnums",
            (ref SliceEncoder e, BasicEnums v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<UncheckedEnums>(
            "UncheckedEnums",
            (ref SliceEncoder e, UncheckedEnums v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Shape>("Shape", (ref SliceEncoder e, Shape v) => e.EncodeShape(v), (ref SliceDecoder d) => d.DecodeShape()),
        Type<Shapes>("Shapes", (ref SliceEncoder e, Shapes v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<Message>(
            "Message",
            (ref SliceEncoder e, Message v) => e.EncodeMessage(v),
            (ref SliceDecoder d) => d.DecodeMessage()),
        Type<Messages>(
            "Messages",
            (ref SliceEncoder e, Messages v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<CompactShape>(
            "CompactShape",
            (ref SliceEncoder e, CompactShape v) => e.EncodeCompactShape(v),
            (ref SliceDecoder d) => d.DecodeCompactShape()),
        Type<CompactShapes>(
            "CompactShapes",
            (ref SliceEncoder e, CompactShapes v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Event>("Event", (ref SliceEncoder e, Event v) => e.EncodeEvent(v), (ref SliceDecoder d) => d.DecodeEvent()),
        Type<Events>("Events", (ref SliceEncoder e, Events v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<RevisedEvent>(
            "RevisedEvent",
            (ref SliceEncoder e, RevisedEvent v) => e.EncodeRevisedEvent(v),
            (ref SliceDecoder d) => d.DecodeRevisedEvent()),
        Type<Fraction>(
            "Fraction",
            (ref SliceEncoder e, Fraction v) => e.EncodeFraction(v),
            (ref SliceDecoder d) => d.DecodeFraction()),
        Type<CustomTypes>(
            "CustomTypes",
            (ref SliceEncoder e, CustomTypes v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<WellKnown>(
            "WellKnown",
            (ref SliceEncoder e, WellKnown v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<ErrorInfo>(
            "ErrorInfo",
            (ref SliceEncoder e, ErrorInfo v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
        Type<Results>("Results", (ref SliceEncoder e, Results v) => v.Encode(ref e), (ref SliceDecoder d) => new(ref d)),
        Type<OptionalResults>(
            "OptionalResults",
            (ref SliceEncoder e, OptionalResults v) => v.Encode(ref e),
            (ref SliceDecoder d) => new(ref d)),
    }.ToDictionary(type => type.Name);

    /// <summary>Finds a type by its Slice name, or returns null.</summary>
    public static ISliceType? Find(string name) => _types.GetValueOrDefault(name);

    private static SliceType<T> Type<T>(string name, EncodeAction<T> encode, DecodeFunc<T> decode) =>
        new($"Conformance::{name}", encode, decode);
}
