// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

await GeneratorDriver.RunAsync(
    generateCode: (symbol, currentNamespace) => symbol switch
    {
        Interface interfaceDef => InterfaceGenerator.Generate(interfaceDef),
        _ => null,
    },
    mapOutputPath: path => Path.ChangeExtension(path, ".IceRpc.cs"),
    usings: ["IceRpc.Slice", "IceRpc.Slice.Operations", "ZeroC.Slice.Codec"]).ConfigureAwait(false);
