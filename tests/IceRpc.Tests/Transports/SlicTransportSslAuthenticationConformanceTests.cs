// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the Ssl duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SlicTransportSslAuthenticationConformanceTests : MultiplexedTransportSslAuthenticationConformanceTests
{
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport())
            .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport())
            .AddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()))
            .AddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.PayloadErrorCodeConverter = IceRpcProtocol.Instance.PayloadErrorCodeConverter);

        return services;
    }
}
