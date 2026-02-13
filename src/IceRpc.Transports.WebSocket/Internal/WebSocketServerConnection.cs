// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>The server-side WebSocket connection implementation.</summary>
internal sealed class WebSocketServerConnection : WebSocketConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private readonly string _path;
    private SslStream? _sslStream;

    internal WebSocketServerConnection(
        Socket socket,
        SslServerAuthenticationOptions? authenticationOptions,
        string path)
        : base(isClient: false)
    {
        Socket = socket;
        _authenticationOptions = authenticationOptions;
        _path = path;
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        try
        {
            if (_authenticationOptions is not null)
            {
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);
                await _sslStream.AuthenticateAsServerAsync(
                    _authenticationOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            await PerformHandshakeAsync(_path, host: "", cancellationToken).ConfigureAwait(false);

            return new TransportConnectionInformation(
                localNetworkAddress: Socket.LocalEndPoint!,
                remoteNetworkAddress: Socket.RemoteEndPoint!,
                _sslStream?.RemoteCertificate);
        }
        catch (InvalidOperationException exception)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "The WebSocket handshake failed.",
                exception);
        }
        catch (FormatException exception)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "The WebSocket handshake failed.",
                exception);
        }
        catch (IOException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }
}
