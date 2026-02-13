// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>Implements <see cref="IDuplexConnection" /> for WebSocket over TCP, with or without TLS.</summary>
internal abstract class WebSocketConnection : IDuplexConnection
{
    internal abstract Socket Socket { get; }

    internal abstract SslStream? SslStream { get; }

    private protected volatile bool _isDisposed;

    private const int ReadBufferSize = 16 * 1024;

    private const string WebSocketUUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly bool _isClient;
    private bool _isShutdown;
    private Stream? _networkStream;
    private readonly byte[] _readBuffer;
    private int _readBufferCount;
    private int _readBufferOffset;
    private bool _readCloseReceived;
    private bool _readMasked;
    private byte[] _readMaskKey = new byte[4];
    private int _readMaskOffset;
    private int _readPayloadRemaining;
    private bool _writeCloseSent;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    private Stream NetworkStream => _networkStream ??=
        SslStream is SslStream sslStream ? sslStream : new NetworkStream(Socket, ownsSocket: false);

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return ConnectAsyncCore(cancellationToken);
    }

    public void Dispose()
    {
        _isDisposed = true;

        if (SslStream is SslStream sslStream)
        {
            sslStream.Dispose();
        }

        if (_isShutdown)
        {
            Socket.Dispose();
        }
        else
        {
            Socket.Close(0);
        }

        _writeSemaphore.Dispose();
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        return buffer.Length > 0 ? PerformReadAsync() :
            throw new ArgumentException($"The {nameof(buffer)} cannot be empty.", nameof(buffer));

        async ValueTask<int> PerformReadAsync()
        {
            try
            {
                while (true)
                {
                    if (_readCloseReceived)
                    {
                        return 0;
                    }

                    // If we have remaining payload bytes from the current frame, deliver them.
                    if (_readPayloadRemaining > 0)
                    {
                        if (_readBufferCount == 0)
                        {
                            // Need more data from the network to deliver payload bytes.
                            await EnsureReadBufferAsync(1, cancellationToken).ConfigureAwait(false);
                        }
                        return ReadPayloadFromBuffer(buffer.Span);
                    }

                    // We need to read a new frame header. Ensure we have at least 2 bytes.
                    await EnsureReadBufferAsync(2, cancellationToken).ConfigureAwait(false);

                    if (!WebSocketFrameHelper.TryReadFrameHeader(
                        _readBuffer.AsSpan(_readBufferOffset, _readBufferCount),
                        out WebSocketFrameHelper.WebSocketFrameHeader header))
                    {
                        // We need more data for the header. Read more and try again.
                        await EnsureReadBufferAsync(
                            WebSocketFrameHelper.MaxHeaderSize,
                            cancellationToken).ConfigureAwait(false);

                        if (!WebSocketFrameHelper.TryReadFrameHeader(
                            _readBuffer.AsSpan(_readBufferOffset, _readBufferCount),
                            out header))
                        {
                            throw new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                "Failed to read WebSocket frame header.");
                        }
                    }

                    // Consume the header bytes.
                    _readBufferOffset += header.HeaderLength;
                    _readBufferCount -= header.HeaderLength;

                    // Validate mask direction per RFC 6455: client-to-server frames must be masked,
                    // server-to-client frames must not be masked.
                    if (!_isClient && !header.Masked)
                    {
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            "Received unmasked frame from client; client frames must be masked.");
                    }
                    if (_isClient && header.Masked)
                    {
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            "Received masked frame from server; server frames must not be masked.");
                    }

                    switch (header.OpCode)
                    {
                        case WebSocketFrameHelper.OpBinary:
                        case WebSocketFrameHelper.OpContinuation:
                        {
                            if (header.PayloadLength <= 0)
                            {
                                throw new IceRpcException(
                                    IceRpcError.ConnectionAborted,
                                    "Received WebSocket data frame with zero payload length.");
                            }

                            _readPayloadRemaining = header.PayloadLength;
                            _readMasked = header.Masked;
                            if (_readMasked)
                            {
                                Array.Copy(header.MaskKey, _readMaskKey, 4);
                            }
                            _readMaskOffset = 0;

                            // Deliver available payload bytes.
                            return ReadPayloadFromBuffer(buffer.Span);
                        }

                        case WebSocketFrameHelper.OpClose:
                        {
                            await SkipPayloadAsync(header.PayloadLength, cancellationToken).ConfigureAwait(false);
                            _readCloseReceived = true;
                            return 0;
                        }

                        case WebSocketFrameHelper.OpPing:
                        {
                            byte[] pingPayload = await ReadControlPayloadAsync(
                                header,
                                cancellationToken).ConfigureAwait(false);
                            await SendPongAsync(pingPayload, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        case WebSocketFrameHelper.OpPong:
                        {
                            await SkipPayloadAsync(header.PayloadLength, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        case WebSocketFrameHelper.OpText:
                        {
                            throw new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                "WebSocket text frames are not supported.");
                        }

                        default:
                        {
                            throw new IceRpcException(
                                IceRpcError.ConnectionAborted,
                                $"Unsupported WebSocket opcode: {header.OpCode}");
                        }
                    }
                }
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

    public Task ShutdownWriteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            try
            {
                await SendCloseAsync(
                    WebSocketFrameHelper.ClosureNormal,
                    cancellationToken).ConfigureAwait(false);

                if (SslStream is SslStream sslStream)
                {
                    Task shutdownTask = sslStream.ShutdownAsync();

                    try
                    {
                        await shutdownTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await AbortAndObserveAsync(shutdownTask).ConfigureAwait(false);
                        throw;
                    }
                }

                Socket.Shutdown(SocketShutdown.Send);
                _isShutdown = true;
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

    public ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            try
            {
                await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    int payloadLength = (int)buffer.Length;

                    byte[]? maskKey = null;
                    if (_isClient)
                    {
                        maskKey = new byte[4];
                        RandomNumberGenerator.Fill(maskKey);
                    }

                    byte[] headerBytes = new byte[WebSocketFrameHelper.MaxHeaderSize];
                    int headerLength = WebSocketFrameHelper.WriteFrameHeader(
                        headerBytes,
                        WebSocketFrameHelper.OpBinary,
                        payloadLength,
                        mask: _isClient,
                        maskKey);

                    await NetworkStream.WriteAsync(
                        headerBytes.AsMemory(0, headerLength),
                        cancellationToken).ConfigureAwait(false);

                    if (_isClient && maskKey is not null)
                    {
                        byte[] tempBuffer = ArrayPool<byte>.Shared.Rent(
                            Math.Min(payloadLength, 16 * 1024));
                        try
                        {
                            int maskOffset = 0;
                            foreach (ReadOnlyMemory<byte> segment in buffer)
                            {
                                int remaining = segment.Length;
                                int segmentOffset = 0;
                                while (remaining > 0)
                                {
                                    int copyLen = Math.Min(remaining, tempBuffer.Length);
                                    segment.Span.Slice(segmentOffset, copyLen).CopyTo(tempBuffer);
                                    WebSocketFrameHelper.ApplyMask(
                                        tempBuffer.AsSpan(0, copyLen),
                                        maskKey,
                                        maskOffset);
                                    await NetworkStream.WriteAsync(
                                        tempBuffer.AsMemory(0, copyLen),
                                        cancellationToken).ConfigureAwait(false);
                                    maskOffset += copyLen;
                                    segmentOffset += copyLen;
                                    remaining -= copyLen;
                                }
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(tempBuffer);
                        }
                    }
                    else
                    {
                        foreach (ReadOnlyMemory<byte> segment in buffer)
                        {
                            await NetworkStream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await NetworkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
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

    private protected WebSocketConnection(bool isClient)
    {
        _isClient = isClient;
        _readBuffer = new byte[ReadBufferSize];
    }

    private protected abstract Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken);

    /// <summary>Performs the WebSocket HTTP upgrade handshake on the underlying stream.</summary>
    private protected async Task PerformHandshakeAsync(
        string path,
        string host,
        CancellationToken cancellationToken)
    {
        if (_isClient)
        {
            await PerformClientHandshakeAsync(path, host, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PerformServerHandshakeAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ComputeWebSocketAccept(string key)
    {
        string input = key + WebSocketUUID;
#pragma warning disable CA5350 // SHA1 is required by the WebSocket protocol (RFC 6455)
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
    }

    private static void ValidateClientHandshakeResponse(HttpParser parser, string key)
    {
        if (parser.VersionMajor != 1 || parser.VersionMinor != 1)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Unsupported HTTP version in WebSocket handshake response.");
        }

        if (parser.Status != 101)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionRefused,
                $"Unexpected HTTP status {parser.Status} in WebSocket handshake response.");
        }

        string? upgrade = parser.GetHeader("Upgrade", toLower: true);
        if (upgrade is null || upgrade != "websocket")
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid or missing Upgrade header in WebSocket handshake response.");
        }

        string? connection = parser.GetHeader("Connection", toLower: true);
        if (connection is null || !connection.Contains("upgrade", StringComparison.Ordinal))
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid or missing Connection header in WebSocket handshake response.");
        }

        string? accept = parser.GetHeader("Sec-WebSocket-Accept", toLower: false);
        if (accept is null)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Missing Sec-WebSocket-Accept header in WebSocket handshake response.");
        }

        string expectedAccept = ComputeWebSocketAccept(key);
        if (!accept.Equals(expectedAccept, StringComparison.Ordinal))
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid Sec-WebSocket-Accept value in WebSocket handshake response.");
        }
    }

    private static string ValidateServerHandshakeRequest(HttpParser parser, string expectedPath)
    {
        if (parser.VersionMajor != 1 || parser.VersionMinor != 1)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Unsupported HTTP version in WebSocket handshake request.");
        }

        if (parser.Method != "GET")
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                $"Unexpected HTTP method '{parser.Method}' in WebSocket handshake request; expected 'GET'.");
        }

        if (parser.Uri != expectedPath)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                $"Unexpected request path '{parser.Uri}' in WebSocket handshake request; expected '{expectedPath}'.");
        }

        string? upgrade = parser.GetHeader("Upgrade", toLower: true);
        if (upgrade is null || upgrade != "websocket")
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid or missing Upgrade header in WebSocket handshake request.");
        }

        string? connection = parser.GetHeader("Connection", toLower: true);
        if (connection is null || !connection.Contains("upgrade", StringComparison.Ordinal))
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid or missing Connection header in WebSocket handshake request.");
        }

        string? version = parser.GetHeader("Sec-WebSocket-Version", toLower: false);
        if (version is null || version != "13")
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                $"Unsupported or missing WebSocket version: {version}.");
        }

        string? key = parser.GetHeader("Sec-WebSocket-Key", toLower: false);
        if (key is null)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Missing Sec-WebSocket-Key header in WebSocket handshake request.");
        }

        byte[] decodedKey = Convert.FromBase64String(key);
        if (decodedKey.Length != 16)
        {
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "Invalid Sec-WebSocket-Key value in WebSocket handshake request.");
        }

        string acceptValue = ComputeWebSocketAccept(key);
        return
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptValue}\r\n" +
            "\r\n";
    }

    private async Task AbortAndObserveAsync(Task task)
    {
        Socket.Close(0);
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // observe exception
        }
    }

    private async ValueTask EnsureReadBufferAsync(int minimumBytes, CancellationToken cancellationToken)
    {
        if (_readBufferOffset > 0 && _readBufferCount < minimumBytes)
        {
            Array.Copy(_readBuffer, _readBufferOffset, _readBuffer, 0, _readBufferCount);
            _readBufferOffset = 0;
        }

        while (_readBufferCount < minimumBytes)
        {
            int freeSpace = _readBuffer.Length - _readBufferOffset - _readBufferCount;
            if (freeSpace == 0)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "WebSocket read buffer overflow.");
            }

            int bytesRead = await NetworkStream.ReadAsync(
                _readBuffer.AsMemory(_readBufferOffset + _readBufferCount, freeSpace),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "Connection closed while reading WebSocket frame.");
            }

            _readBufferCount += bytesRead;
        }
    }

    private async Task PerformClientHandshakeAsync(
        string path,
        string host,
        CancellationToken cancellationToken)
    {
        byte[] keyBytes = new byte[16];
        RandomNumberGenerator.Fill(keyBytes);
        string key = Convert.ToBase64String(keyBytes);

        string request =
            $"GET {path} HTTP/1.1\r\n" +
            $"Host: {host}\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            $"Sec-WebSocket-Key: {key}\r\n" +
            "\r\n";

        byte[] requestBytes = Encoding.UTF8.GetBytes(request);
        await NetworkStream.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
        await NetworkStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var parser = new HttpParser();
        byte[] responseBuffer = new byte[4096];
        int responseLength = 0;

        while (true)
        {
            int bytesRead = await NetworkStream.ReadAsync(
                responseBuffer.AsMemory(responseLength, responseBuffer.Length - responseLength),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "Connection closed during WebSocket handshake.");
            }

            responseLength += bytesRead;

            int messageEnd = HttpParser.IsCompleteMessage(responseBuffer, 0, responseLength);
            if (messageEnd != -1)
            {
                if (!parser.Parse(responseBuffer, 0, messageEnd))
                {
                    throw new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        "Incomplete HTTP response during WebSocket handshake.");
                }

                ValidateClientHandshakeResponse(parser, key);

                int extraBytes = responseLength - messageEnd;
                if (extraBytes > 0)
                {
                    Array.Copy(responseBuffer, messageEnd, _readBuffer, 0, extraBytes);
                    _readBufferOffset = 0;
                    _readBufferCount = extraBytes;
                }
                break;
            }

            if (responseLength == responseBuffer.Length)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "WebSocket handshake response too large.");
            }
        }
    }

    private async Task PerformServerHandshakeAsync(string expectedPath, CancellationToken cancellationToken)
    {
        var parser = new HttpParser();
        byte[] requestBuffer = new byte[4096];
        int requestLength = 0;

        while (true)
        {
            int bytesRead = await NetworkStream.ReadAsync(
                requestBuffer.AsMemory(requestLength, requestBuffer.Length - requestLength),
                cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "Connection closed during WebSocket handshake.");
            }

            requestLength += bytesRead;

            int messageEnd = HttpParser.IsCompleteMessage(requestBuffer, 0, requestLength);
            if (messageEnd != -1)
            {
                if (!parser.Parse(requestBuffer, 0, messageEnd))
                {
                    throw new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        "Incomplete HTTP request during WebSocket handshake.");
                }

                string responseStr = ValidateServerHandshakeRequest(parser, expectedPath);

                byte[] responseBytes = Encoding.UTF8.GetBytes(responseStr);
                await NetworkStream.WriteAsync(responseBytes, cancellationToken).ConfigureAwait(false);
                await NetworkStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                int extraBytes = requestLength - messageEnd;
                if (extraBytes > 0)
                {
                    Array.Copy(requestBuffer, messageEnd, _readBuffer, 0, extraBytes);
                    _readBufferOffset = 0;
                    _readBufferCount = extraBytes;
                }
                break;
            }

            if (requestLength == requestBuffer.Length)
            {
                throw new IceRpcException(
                    IceRpcError.ConnectionAborted,
                    "WebSocket handshake request too large.");
            }
        }
    }

    private async ValueTask<byte[]> ReadControlPayloadAsync(
        WebSocketFrameHelper.WebSocketFrameHeader header,
        CancellationToken cancellationToken)
    {
        if (header.PayloadLength == 0)
        {
            return [];
        }

        await EnsureReadBufferAsync(header.PayloadLength, cancellationToken).ConfigureAwait(false);

        byte[] payload = new byte[header.PayloadLength];
        _readBuffer.AsSpan(_readBufferOffset, header.PayloadLength).CopyTo(payload);

        if (header.Masked)
        {
            WebSocketFrameHelper.ApplyMask(payload, header.MaskKey, 0);
        }

        _readBufferOffset += header.PayloadLength;
        _readBufferCount -= header.PayloadLength;

        return payload;
    }

    private int ReadPayloadFromBuffer(Span<byte> destination)
    {
        int available = Math.Min(_readBufferCount, _readPayloadRemaining);
        int toDeliver = Math.Min(available, destination.Length);

        if (toDeliver > 0)
        {
            _readBuffer.AsSpan(_readBufferOffset, toDeliver).CopyTo(destination);

            if (_readMasked)
            {
                WebSocketFrameHelper.ApplyMask(destination.Slice(0, toDeliver), _readMaskKey, _readMaskOffset);
            }

            _readBufferOffset += toDeliver;
            _readBufferCount -= toDeliver;
            _readPayloadRemaining -= toDeliver;
            _readMaskOffset += toDeliver;
        }

        return toDeliver;
    }

    private async ValueTask SendCloseAsync(ushort reasonCode, CancellationToken cancellationToken)
    {
        if (_writeCloseSent)
        {
            return;
        }
        _writeCloseSent = true;

        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] closePayload = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(closePayload, reasonCode);

            byte[]? maskKey = null;
            if (_isClient)
            {
                maskKey = new byte[4];
                RandomNumberGenerator.Fill(maskKey);
            }

            byte[] headerBytes = new byte[WebSocketFrameHelper.MaxHeaderSize];
            int headerLength = WebSocketFrameHelper.WriteFrameHeader(
                headerBytes,
                WebSocketFrameHelper.OpClose,
                closePayload.Length,
                mask: _isClient,
                maskKey);

            await NetworkStream.WriteAsync(
                headerBytes.AsMemory(0, headerLength),
                cancellationToken).ConfigureAwait(false);

            if (_isClient && maskKey is not null)
            {
                WebSocketFrameHelper.ApplyMask(closePayload, maskKey, 0);
            }
            await NetworkStream.WriteAsync(closePayload, cancellationToken).ConfigureAwait(false);
            await NetworkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private async ValueTask SendPongAsync(byte[] payload, CancellationToken cancellationToken)
    {
        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[]? maskKey = null;
            if (_isClient)
            {
                maskKey = new byte[4];
                RandomNumberGenerator.Fill(maskKey);
            }

            byte[] headerBytes = new byte[WebSocketFrameHelper.MaxHeaderSize];
            int headerLength = WebSocketFrameHelper.WriteFrameHeader(
                headerBytes,
                WebSocketFrameHelper.OpPong,
                payload.Length,
                mask: _isClient,
                maskKey);

            await NetworkStream.WriteAsync(
                headerBytes.AsMemory(0, headerLength),
                cancellationToken).ConfigureAwait(false);

            if (payload.Length > 0)
            {
                if (_isClient && maskKey is not null)
                {
                    byte[] maskedPayload = new byte[payload.Length];
                    payload.AsSpan().CopyTo(maskedPayload);
                    WebSocketFrameHelper.ApplyMask(maskedPayload, maskKey, 0);
                    await NetworkStream.WriteAsync(maskedPayload, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await NetworkStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                }
            }

            await NetworkStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private async ValueTask SkipPayloadAsync(int payloadLength, CancellationToken cancellationToken)
    {
        int remaining = payloadLength;
        while (remaining > 0)
        {
            if (_readBufferCount == 0)
            {
                await EnsureReadBufferAsync(1, cancellationToken).ConfigureAwait(false);
            }
            int toSkip = Math.Min(_readBufferCount, remaining);
            _readBufferOffset += toSkip;
            _readBufferCount -= toSkip;
            remaining -= toSkip;
        }
    }
}
