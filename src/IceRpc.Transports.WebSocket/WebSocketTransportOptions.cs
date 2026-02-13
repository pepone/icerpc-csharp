// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.WebSocket;

/// <summary>The base options class for WebSocket transports.</summary>
public record class WebSocketTransportOptions
{
    /// <summary>Gets or sets a value indicating whether the underlying socket is using the Nagle algorithm.
    /// </summary>
    /// <value><see langword="false" /> if the socket uses the Nagle algorithm; otherwise, <see langword="true" />.
    /// Defaults to <see langword="true" />.</value>
    public bool NoDelay { get; set; } = true;

    /// <summary>Gets or sets the socket receive buffer size in bytes.</summary>
    /// <value>The receive buffer size in bytes. It can't be less than <c>1</c> KB. <see langword="null" /> means use
    /// the operating system default. Defaults to <see langword="null" />.</value>
    public int? ReceiveBufferSize
    {
        get => _receiveBufferSize;
        set => _receiveBufferSize = value is null || value >= 1024 ? value :
            throw new ArgumentException(
                $"The {nameof(ReceiveBufferSize)} value cannot be less than 1KB.",
                nameof(value));
    }

    /// <summary>Gets or sets the socket send buffer size in bytes.</summary>
    /// <value>The send buffer size in bytes. It can't be less than <c>1</c> KB. <see langword="null" /> means use the
    /// OS default. Defaults to <see langword="null" />.
    /// </value>
    public int? SendBufferSize
    {
        get => _sendBufferSize;
        set => _sendBufferSize = value is null || value >= 1024 ? value :
            throw new ArgumentException(
                $"The {nameof(SendBufferSize)} value cannot be less than 1KB.",
                nameof(value));
    }

    private int? _receiveBufferSize;
    private int? _sendBufferSize;
}
