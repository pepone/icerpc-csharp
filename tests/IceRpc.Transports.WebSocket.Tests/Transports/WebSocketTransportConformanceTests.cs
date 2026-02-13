// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the WebSocket transport connection.</summary>
[Parallelizable(ParallelScope.All)]
public class WebSocketConnectionConformanceTests : DuplexConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddWebSocketTest(listenBacklog);

    protected override bool UsesListenBacklog => true;
}

/// <summary>Conformance tests for the WebSocket transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class WebSocketListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddWebSocketTest(listenBacklog);

    protected override bool UsesListenBacklog => true;
}
