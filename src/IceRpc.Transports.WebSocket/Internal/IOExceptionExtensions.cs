// Copyright (c) ZeroC, Inc.

using System.Net.Sockets;

namespace IceRpc.Transports.WebSocket.Internal;

internal static class IOExceptionExtensions
{
    /// <summary>Converts an IOException into an <see cref="IceRpcException" />.</summary>
    internal static IceRpcException ToIceRpcException(this IOException exception) =>
        exception.InnerException is SocketException socketException ?
            socketException.ToIceRpcException(exception) :
            new IceRpcException(IceRpcError.IceRpcError, exception);
}
