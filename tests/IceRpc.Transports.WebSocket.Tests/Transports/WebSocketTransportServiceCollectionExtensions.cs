// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests.Transports;

internal static class WebSocketTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddWebSocketTest(
        this IServiceCollection services,
        int? listenBacklog,
        Uri? serverAddressUri = null) => services
        .AddDuplexTransportTest(serverAddressUri ?? new Uri("icerpc://127.0.0.1:0/?transport=ws"))
        .AddWebSocketTransport()
        .AddSingleton<WebSocketServerTransportOptions>(
            _ => listenBacklog is null ? new() : new() { ListenBacklog = listenBacklog.Value });

    internal static IServiceCollection AddWebSocketTransport(this IServiceCollection serviceCollection) =>
        serviceCollection
            .AddSingleton<WebSocketClientTransportOptions>()
            .AddSingleton<WebSocketServerTransportOptions>()
            .AddSingleton<IDuplexServerTransport>(
                provider => new WebSocketServerTransport(
                    provider.GetRequiredService<WebSocketServerTransportOptions>()))
            .AddSingleton<IDuplexClientTransport>(
                provider => new WebSocketClientTransport(
                    provider.GetRequiredService<WebSocketClientTransportOptions>()));
}
