// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.WebSocket.Internal;
using System.Net.Security;

namespace IceRpc.Transports.WebSocket;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the WebSocket transport.</summary>
public class WebSocketClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => WsName;

    private const string WsName = "ws";

    private readonly WebSocketClientTransportOptions _options;

    /// <summary>Constructs a <see cref="WebSocketClientTransport" />.</summary>
    public WebSocketClientTransport()
        : this(new WebSocketClientTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="WebSocketClientTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public WebSocketClientTransport(WebSocketClientTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (serverAddress.Transport is string transport && transport != WsName)
        {
            throw new NotSupportedException(
                $"The WebSocket client transport does not support server addresses with transport '{transport}'.");
        }

        if (!CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the WebSocket client transport.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        // Extract path from params, default to "/".
        string path = "/";
        if (serverAddress.Params.TryGetValue("path", out string? pathValue))
        {
            path = pathValue;
        }

        SslClientAuthenticationOptions? authenticationOptions = clientAuthenticationOptions?.Clone();
        if (authenticationOptions is not null)
        {
            authenticationOptions.TargetHost ??= serverAddress.Host;

            if (authenticationOptions.ApplicationProtocols is null &&
                serverAddress.Port != serverAddress.Protocol.DefaultPort)
            {
                authenticationOptions.ApplicationProtocols =
                [
                    new SslApplicationProtocol(serverAddress.Protocol.Name)
                ];
            }
        }

        return new WebSocketClientConnection(
            serverAddress,
            authenticationOptions,
            _options,
            path);

        static bool CheckParams(ServerAddress serverAddress)
        {
            foreach (string name in serverAddress.Params.Keys)
            {
                if (name != "path")
                {
                    return false;
                }
            }
            return true;
        }
    }
}
