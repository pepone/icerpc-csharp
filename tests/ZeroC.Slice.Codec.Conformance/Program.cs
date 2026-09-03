// Copyright (c) ZeroC, Inc.

// The slice-codec-csharp test executable.
//
//   slice-codec-csharp encode <test-case.json>   writes the Slice-encoded value of the test case to stdout
//   slice-codec-csharp decode <test-case.json>   reads a Slice payload from stdin and compares it with the value
//
// Exit status: 0 = success, 1 = failure, 2 = usage error, 3 = unsupported test case.

using System.IO.Pipelines;
using ZeroC.Slice.Codec;
using ZeroC.Slice.Codec.Conformance;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitUnsupported = 3;

    private static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "encode" && args[0] != "decode"))
        {
            Console.Error.WriteLine("usage: slice-codec-csharp (encode|decode) <test-case.json>");
            return ExitUsage;
        }

        string command = args[0];
        string path = args[1];

        TestCaseFile test;
        try
        {
            test = TestCaseFile.Load(path);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"test: {Path.GetFileNameWithoutExtension(path)}");
            Console.Error.WriteLine($"cannot read the test case: {exception.Message}");
            return ExitFailure;
        }

        string typeName = command == "encode" ? test.Type : test.DecodeType;
        ISliceType? type = TypeRegistry.Find(typeName);
        if (type is null)
        {
            Console.Error.WriteLine($"test: {test.Id}");
            Console.Error.WriteLine($"unsupported type: {typeName}");
            return ExitUnsupported;
        }

        try
        {
            return command == "encode" ? Encode(test, type) : Decode(test, type);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"test: {test.Id}");
            Console.Error.WriteLine($"{command} failed: {exception}");
            return ExitFailure;
        }
    }

    private static int Encode(TestCaseFile test, ISliceType type)
    {
        object? value = type.FromJson(test.Value);

        // SliceEncoder assumes the buffer writer never relocates memory it already handed out (it fills in size
        // placeholders after the fact). ArrayBufferWriter<byte> breaks this assumption when it grows, so we use a
        // Pipe, like IceRPC itself does.
        var pipe = new Pipe();
        var encoder = new SliceEncoder(pipe.Writer);
        type.Encode(ref encoder, value);
        pipe.Writer.Complete();

        using Stream stdout = Console.OpenStandardOutput();
        if (pipe.Reader.TryRead(out ReadResult result))
        {
            foreach (ReadOnlyMemory<byte> segment in result.Buffer)
            {
                stdout.Write(segment.Span);
            }
            pipe.Reader.AdvanceTo(result.Buffer.End);
        }
        pipe.Reader.Complete();
        stdout.Flush();
        return ExitSuccess;
    }

    private static int Decode(TestCaseFile test, ISliceType type)
    {
        object? expected = type.FromJson(test.DecodedValue);

        byte[] payload;
        using (Stream stdin = Console.OpenStandardInput())
        using (var buffer = new MemoryStream())
        {
            stdin.CopyTo(buffer);
            payload = buffer.ToArray();
        }

        object? actual;
        try
        {
            var decoder = new SliceDecoder(payload);
            actual = type.Decode(ref decoder);
            decoder.CheckEndOfBuffer();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"test: {test.Id}");
            Console.Error.WriteLine($"decoding failed: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine($"payload ({payload.Length} bytes): {ValueFormatter.Hex(payload)}");
            return ExitFailure;
        }

        List<string> differences = StructuralComparer.Compare(expected, actual);
        if (differences.Count == 0)
        {
            return ExitSuccess;
        }

        Console.Error.WriteLine($"test: {test.Id}");
        foreach (string difference in differences)
        {
            Console.Error.WriteLine(difference);
        }
        return ExitFailure;
    }
}
