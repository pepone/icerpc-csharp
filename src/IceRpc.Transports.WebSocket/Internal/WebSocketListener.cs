// Copyright (c) ZeroC, Inc.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>The listener implementation for the WebSocket transport.</summary>
internal sealed class WebSocketListener : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly SslServerAuthenticationOptions? _authenticationOptions;
    private volatile int _disposed;
    private readonly string _path;
    private readonly Socket _socket;

    public async Task<(IDuplexConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            Socket acceptedSocket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);

            var connection = new WebSocketServerConnection(
                acceptedSocket,
                _authenticationOptions,
                _path);
            return (connection, acceptedSocket.RemoteEndPoint!);
        }
        catch (SocketException exception)
        {
            if (exception.SocketErrorCode == SocketError.OperationAborted)
            {
                ObjectDisposedException.ThrowIf(_disposed == 1, this);
            }
            throw exception.ToIceRpcException();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _socket.Dispose();
        }
        return default;
    }

    internal WebSocketListener(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? authenticationOptions,
        WebSocketServerTransportOptions wsOptions,
        string path)
    {
        _path = path;
        if (!IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress))
        {
            throw new ArgumentException(
                $"Listening on the DNS name '{serverAddress.Host}' is not allowed; an IP address is required.",
                nameof(serverAddress));
        }

        _authenticationOptions = authenticationOptions?.Clone();

        if (_authenticationOptions is not null && _authenticationOptions.ApplicationProtocols is null)
        {
            _authenticationOptions.ApplicationProtocols = new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol(serverAddress.Protocol.Name)
            };
        }

        var address = new IPEndPoint(ipAddress, serverAddress.Port);

        // When using IPv6 address family we use the socket constructor without AddressFamily parameter to ensure
        // dual-mode sockets are used on platforms that support them.
        _socket = ipAddress.AddressFamily == AddressFamily.InterNetwork ?
            new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) :
            new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.ExclusiveAddressUse = true;
            _socket.Configure(wsOptions);
            _socket.Bind(address);
            address = (IPEndPoint)_socket.LocalEndPoint!;
            _socket.Listen(wsOptions.ListenBacklog);
        }
        catch (SocketException exception)
        {
            _socket.Dispose();
            throw exception.ToIceRpcException();
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        ServerAddress = serverAddress with { Port = (ushort)address.Port };
    }
}
