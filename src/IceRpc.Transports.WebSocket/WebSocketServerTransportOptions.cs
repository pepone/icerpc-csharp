// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.WebSocket;

/// <summary>The options class for configuring <see cref="WebSocketServerTransport" />.</summary>
public sealed record class WebSocketServerTransportOptions : WebSocketTransportOptions
{
    /// <summary>Gets or sets the length of the server socket queue for accepting new connections. If a new connection
    /// request arrives and the queue is full, the client connection establishment will fail with a <see
    /// cref="IceRpcException" /> and the <see cref="IceRpcError.ConnectionRefused" /> error code.</summary>
    /// <value>The server socket backlog size. Defaults to <c>511</c>.</value>
    public int ListenBacklog
    {
        get => _listenBacklog;
        set => _listenBacklog = value > 0 ? value :
            throw new ArgumentException($"The {nameof(ListenBacklog)} value cannot be less than 1", nameof(value));
    }

    private int _listenBacklog = 511;
}
