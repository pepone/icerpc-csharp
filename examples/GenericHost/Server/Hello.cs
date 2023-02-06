// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace GenericHostExample;

public class Hello : Service, IHello
{
    public async ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}
