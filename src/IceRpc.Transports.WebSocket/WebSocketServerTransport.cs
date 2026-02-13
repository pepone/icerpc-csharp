// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.WebSocket.Internal;
using System.Net.Security;

namespace IceRpc.Transports.WebSocket;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the WebSocket transport.</summary>
public class WebSocketServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => WsName;

    private const string WsName = "ws";

    private readonly WebSocketServerTransportOptions _options;

    /// <summary>Constructs a <see cref="WebSocketServerTransport" />.</summary>
    public WebSocketServerTransport()
        : this(new WebSocketServerTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="WebSocketServerTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public WebSocketServerTransport(WebSocketServerTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (serverAddress.Transport is string transport && transport != WsName)
        {
            throw new NotSupportedException(
                $"The WebSocket server transport does not support server addresses with transport '{transport}'.");
        }

        if (!CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the WebSocket server transport.",
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

        return new WebSocketListener(serverAddress, options, serverAuthenticationOptions, _options, path);

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
