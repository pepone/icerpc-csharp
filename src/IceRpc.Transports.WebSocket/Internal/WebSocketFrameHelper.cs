// Copyright (c) ZeroC, Inc.

// Extracted from Ice.Internal.WSTransceiver frame handling logic.

using System.Buffers.Binary;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>WebSocket frame opcodes and flags.</summary>
internal static class WebSocketFrameHelper
{
    // WebSocket opcodes
    internal const byte OpContinuation = 0x0;
    internal const byte OpText = 0x1;
    internal const byte OpBinary = 0x2;
    internal const byte OpClose = 0x8;
    internal const byte OpPing = 0x9;
    internal const byte OpPong = 0xA;

    // WebSocket flags
    internal const byte FlagFin = 0x80;
    internal const byte FlagMask = 0x80;

    // Close reason codes
    internal const ushort ClosureNormal = 1000;

    // Maximum frame header size: 2 (base) + 8 (extended payload length) + 4 (mask key) = 14
    internal const int MaxHeaderSize = 14;

    /// <summary>A parsed WebSocket frame header.</summary>
    internal readonly struct WebSocketFrameHeader
    {
        internal byte OpCode { get; init; }

        internal bool Fin { get; init; }

        internal bool Masked { get; init; }

        internal int PayloadLength { get; init; }

        internal byte[] MaskKey { get; init; }

        internal int HeaderLength { get; init; }
    }

    /// <summary>Tries to read a WebSocket frame header from the buffer.</summary>
    /// <returns><see langword="true"/> if a complete header was read.</returns>
    internal static bool TryReadFrameHeader(ReadOnlySpan<byte> buffer, out WebSocketFrameHeader header)
    {
        header = default;

        if (buffer.Length < 2)
        {
            return false;
        }

        byte firstByte = buffer[0];
        byte secondByte = buffer[1];

        bool fin = (firstByte & FlagFin) != 0;
        byte opCode = (byte)(firstByte & 0x0F);
        bool masked = (secondByte & FlagMask) != 0;
        int payloadLength = secondByte & 0x7F;

        int headerLength = 2;

        if (payloadLength == 126)
        {
            headerLength += 2;
            if (buffer.Length < headerLength + (masked ? 4 : 0))
            {
                return false;
            }
            payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));
        }
        else if (payloadLength == 127)
        {
            headerLength += 8;
            if (buffer.Length < headerLength + (masked ? 4 : 0))
            {
                return false;
            }
            long longLength = BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(2));
            if (longLength < 0 || longLength > int.MaxValue)
            {
                throw new InvalidOperationException($"Invalid WebSocket payload length: {longLength}");
            }
            payloadLength = (int)longLength;
        }
        else
        {
            if (buffer.Length < headerLength + (masked ? 4 : 0))
            {
                return false;
            }
        }

        byte[] maskKey = new byte[4];
        if (masked)
        {
            buffer.Slice(headerLength, 4).CopyTo(maskKey);
            headerLength += 4;
        }

        header = new WebSocketFrameHeader
        {
            OpCode = opCode,
            Fin = fin,
            Masked = masked,
            PayloadLength = payloadLength,
            MaskKey = maskKey,
            HeaderLength = headerLength,
        };
        return true;
    }

    /// <summary>Writes a WebSocket frame header into the provided buffer.</summary>
    /// <returns>The number of header bytes written.</returns>
    internal static int WriteFrameHeader(
        Span<byte> headerBuffer,
        byte opCode,
        int payloadLength,
        bool mask,
        byte[]? maskKey)
    {
        int pos = 0;

        // Set the opcode with FIN bit (single frame).
        headerBuffer[pos++] = (byte)(opCode | FlagFin);

        // Set the payload length.
        if (payloadLength <= 125)
        {
            headerBuffer[pos++] = (byte)(payloadLength | (mask ? FlagMask : 0));
        }
        else if (payloadLength <= 65535)
        {
            headerBuffer[pos++] = (byte)(126 | (mask ? FlagMask : 0));
            BinaryPrimitives.WriteUInt16BigEndian(headerBuffer.Slice(pos), (ushort)payloadLength);
            pos += 2;
        }
        else
        {
            headerBuffer[pos++] = (byte)(127 | (mask ? FlagMask : 0));
            BinaryPrimitives.WriteInt64BigEndian(headerBuffer.Slice(pos), payloadLength);
            pos += 8;
        }

        if (mask && maskKey is not null)
        {
            maskKey.AsSpan().CopyTo(headerBuffer.Slice(pos));
            pos += 4;
        }

        return pos;
    }

    /// <summary>Applies or removes a WebSocket mask in-place.</summary>
    /// <param name="data">The data to mask/unmask.</param>
    /// <param name="mask">The 4-byte mask key.</param>
    /// <param name="offset">The byte offset within the logical payload (for continuation across calls).</param>
    internal static void ApplyMask(Span<byte> data, byte[] mask, int offset)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= mask[(offset + i) % 4];
        }
    }
}
