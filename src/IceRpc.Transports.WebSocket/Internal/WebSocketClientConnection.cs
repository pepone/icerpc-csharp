// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>The client-side WebSocket connection implementation.</summary>
internal sealed class WebSocketClientConnection : WebSocketConnection
{
    internal override Socket Socket { get; }

    internal override SslStream? SslStream => _sslStream;

    private readonly EndPoint _addr;
    private readonly SslClientAuthenticationOptions? _authenticationOptions;
    private readonly string _host;
    private readonly string _path;
    private SslStream? _sslStream;

    internal WebSocketClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? authenticationOptions,
        WebSocketClientTransportOptions options,
        string path)
        : base(isClient: true)
    {
        _addr = IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, serverAddress.Port) :
            new DnsEndPoint(serverAddress.Host, serverAddress.Port);

        _authenticationOptions = authenticationOptions;
        _host = serverAddress.Host + (serverAddress.Port != 0 ? $":{serverAddress.Port}" : "");
        _path = path;

        // When using IPv6 address family we use the socket constructor without AddressFamily parameter to ensure
        // dual-mode sockets are used on platforms that support them.
        Socket = ipAddress?.AddressFamily == AddressFamily.InterNetwork ?
            new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) :
            new Socket(SocketType.Stream, ProtocolType.Tcp);

        try
        {
            if (options.LocalNetworkAddress is IPEndPoint localNetworkAddress)
            {
                Socket.Bind(localNetworkAddress);
            }

            Socket.Configure(options);
        }
        catch (SocketException exception)
        {
            Socket.Dispose();
            throw exception.ToIceRpcException();
        }
        catch
        {
            Socket.Dispose();
            throw;
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        try
        {
            Debug.Assert(Socket is not null);

            await Socket.ConnectAsync(_addr, cancellationToken).ConfigureAwait(false);

            // Workaround for https://github.com/dotnet/runtime/issues/75889.
            cancellationToken.ThrowIfCancellationRequested();

            if (_authenticationOptions is not null)
            {
                _sslStream = new SslStream(new NetworkStream(Socket, false), false);
                await _sslStream.AuthenticateAsClientAsync(
                    _authenticationOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            await PerformHandshakeAsync(_path, _host, cancellationToken).ConfigureAwait(false);

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
