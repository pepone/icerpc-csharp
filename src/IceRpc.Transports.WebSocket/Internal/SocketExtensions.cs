// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.WebSocket.Internal;

internal static class SocketExtensions
{
    /// <summary>Configures a socket with the WebSocket transport options.</summary>
    internal static void Configure(this Socket socket, WebSocketTransportOptions options)
    {
        socket.NoDelay = options.NoDelay;

        if (options.ReceiveBufferSize is int receiveSize)
        {
            socket.ReceiveBufferSize = receiveSize;
        }
        if (options.SendBufferSize is int sendSize)
        {
            socket.SendBufferSize = sendSize;
        }
    }
}
