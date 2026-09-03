# Slice codec conformance test executable

This project builds `slice-codec-csharp`, the codec test executable that the language-independent
[slice-conformance](https://github.com/icerpc/slice-conformance-tests) controller drives to check the wire
compatibility of this Slice implementation with other implementations.

The executable reads a test-case file of the conformance suite (a Slice type and a value in the suite's JSON
notation) and encodes or decodes it with the code generated from the suite's Slice definitions:

```shell
slice-codec-csharp encode <test-case.json>   # writes the Slice-encoded value to stdout
slice-codec-csharp decode <test-case.json>   # reads a payload from stdin and compares it with the value
```

Exit status: 0 = success, 1 = failure, 2 = usage error, 3 = unsupported type.

`JsonValues` converts the JSON value into the generated types by reflection (structs through their constructors,
variant enums through their nested record classes, and so on), so supporting a new Slice type of the suite takes one
line in `TypeRegistry`.

## Building

The project needs a checkout of `slice-conformance-tests`, by default next to this repository; pass
`-p:SliceConformanceDir=<path>` otherwise:

```shell
dotnet build tests/ZeroC.Slice.Codec.Conformance -p:SliceConformanceDir=/path/to/slice-conformance-tests
```

The project is not part of `IceRpc.slnx` because of this external dependency. The conformance controller builds
and runs it:

```shell
cd ../slice-conformance-tests
uv run slice-conformance --build
```

## Notes

- `SliceEncoder` fills in size placeholders after the fact and assumes the `IBufferWriter<byte>` never relocates
  memory it already handed out. `ArrayBufferWriter<byte>` breaks this assumption when it grows, so the encoder side
  uses a `Pipe`.
- The decoder side checks that the whole payload was consumed, then compares the decoded value structurally
  (properties, sequences, dictionaries, floats by bit pattern) and reports every difference with its path.
