// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports.WebSocket;

/// <summary>The options class for configuring <see cref="WebSocketClientTransport" />.</summary>
public sealed record class WebSocketClientTransportOptions : WebSocketTransportOptions
{
    /// <summary>Gets or sets the address and port represented by a .NET <see cref="IPEndPoint"/> to use for a client
    /// socket. If specified the client socket will bind to this address and port before connection establishment.
    /// </summary>
    /// <value>The address and port to bind the socket to. Defaults to <see langword="null" />.</value>
    public IPEndPoint? LocalNetworkAddress { get; set; }
}
