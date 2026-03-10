// Copyright (c) ZeroC, Inc.

using System.IO.Pipelines;
using IceRpc.SlicecGen;
using ZeroC.Slice.Codec;
using ZeroC.Slice.Compiler;

// The Slice compiler executes this program and writes the Slice2-encoded request to stdin.

using Stream stdin = Console.OpenStandardInput();
var reader = PipeReader.Create(stdin);

// Read until the Slice compiler closes stdin.
ReadResult readResult;
do
{
    readResult = await reader.ReadAsync().ConfigureAwait(false);
    if (!readResult.IsCompleted)
    {
        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
    }
}
while (!readResult.IsCompleted);

var decoder = new SliceDecoder(
    readResult.Buffer,
    SliceEncoding.Slice2,
    maxCollectionAllocation: (int)readResult.Buffer.Length * 64);
string op = decoder.DecodeString();

// Decode source files and reference files.
var sourceFiles = decoder.DecodeSequence<SliceFile>((ref decoder) => new SliceFile(ref decoder));
var referenceFiles = decoder.DecodeSequence<SliceFile>((ref decoder) => new SliceFile(ref decoder));

reader.AdvanceTo(readResult.Buffer.End);
await reader.CompleteAsync().ConfigureAwait(false);

// Pass 1: Build type registry from ALL files.
var registry = new TypeRegistry(sourceFiles.Concat(referenceFiles));

// Pass 2: Generate code for each source file.
foreach (SliceFile file in sourceFiles)
{
    foreach (Symbol symbol in file.Contents)
    {
        if (symbol is Symbol.Struct structSymbol)
        {
            var code = StructGenerator.GenerateStruct(structSymbol.V, file, registry);
            Console.Error.WriteLine(code.ToString());
        }
    }
}
