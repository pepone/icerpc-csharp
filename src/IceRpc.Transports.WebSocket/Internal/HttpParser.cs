// Copyright (c) ZeroC, Inc.

// Ported from Ice.Internal.HttpParser with adaptations:
// - ByteBuffer replaced with byte[] / ReadOnlySpan<byte>
// - Ice exceptions replaced with InvalidOperationException

using System.Diagnostics;
using System.Text;

namespace IceRpc.Transports.WebSocket.Internal;

/// <summary>An incremental HTTP/1.1 parser for WebSocket upgrade handshake messages.</summary>
internal sealed class HttpParser
{
    /// <summary>Identifies the kind of HTTP message being parsed.</summary>
    internal enum Type
    {
        /// <summary>The message type has not been determined yet (parsing has not started or no distinguishing
        /// bytes have been consumed).</summary>
        Unknown,

        /// <summary>The message is an HTTP request (e.g. "GET /path HTTP/1.1").</summary>
        Request,

        /// <summary>The message is an HTTP response (e.g. "HTTP/1.1 101 Switching Protocols").</summary>
        Response,
    }

    // State machine for incremental HTTP/1.1 parsing. The parser advances one byte at a time through
    // these states. Arrows show the primary transitions; error paths throw InvalidOperationException.
    //
    // High-level flow:
    //
    //   Init ──► Type ──► TypeCheck ──┬──► Request  ──► RequestMethod ──► RequestMethodSP ──► RequestURI
    //                                 │                  ──► RequestURISP ──► Version ──► ... ──► RequestLF
    //                                 │                  ──► HeaderFieldStart ──► ... ──► Complete
    //                                 │
    //                                 └──► Response ──► VersionHT ──► ... ──► VersionMinor
    //                                       ──► ResponseVersionSP ──► ResponseStatus
    //                                       ──► ResponseReasonStart ──► ResponseReason ──► ResponseLF
    //                                       ──► HeaderFieldStart ──► ... ──► Complete
    private enum State
    {
        // -- Initialization & type detection --
        Init,                   // Reset all fields; immediately transition to Type.
        Type,                   // First non-CRLF byte: 'H' → TypeCheck, anything else → Request.
        TypeCheck,              // Second byte after 'H': 'T' → Response ("HTTP/..."), 'E' → Request ("HEAD").

        // -- Request line: "METHOD URI HTTP/x.y\r\n" --
        Request,                // Set _type = Request, then fall through to RequestMethod.
        RequestMethod,          // Accumulate method characters until SP/CR/LF.
        RequestMethodSP,        // Consume spaces between method and URI.
        RequestURI,             // Accumulate URI characters until SP/CR/LF.
        RequestURISP,           // Consume spaces between URI and version string.
        RequestLF,              // Expect LF after CR that terminates the request line.

        // -- Header fields (shared by requests and responses) --
        HeaderFieldStart,       // Start of a new header line, or CR/LF marking end of headers.
        HeaderFieldContStart,   // Leading spaces of an RFC 7230 obs-fold continuation line.
        HeaderFieldCont,        // Continuation line value bytes.
        HeaderFieldNameStart,   // Record start position for a new header name.
        HeaderFieldName,        // Accumulate header name characters until ':' or SP.
        HeaderFieldNameEnd,     // Finalize name (lowercased); expect ':'.
        HeaderFieldValueStart,  // Skip optional spaces after ':'; begin capturing value.
        HeaderFieldValue,       // Accumulate header value characters until CR/LF.
        HeaderFieldValueEnd,    // Store the completed value; handle multi-valued headers with ", ".
        HeaderFieldLF,          // Expect LF after CR at the end of a header line.
        HeaderFieldEndLF,       // Expect LF after a lone CR that ends the header block.

        // -- HTTP version string: "HTTP/x.y" (character-by-character) --
        Version,                // Expect 'H'.
        VersionH,               // Expect 'T'.
        VersionHT,              // Expect 'T'.
        VersionHTT,             // Expect 'P'.
        VersionHTTP,            // Expect '/'.
        VersionMajor,           // Accumulate major version digits until '.'.
        VersionMinor,           // Accumulate minor version digits; then branch by message type.

        // -- Response status line: "HTTP/x.y STATUS REASON\r\n" --
        Response,               // Set _type = Response, then continue at VersionHT (already consumed "HT").
        ResponseVersionSP,      // Consume spaces between version and status code.
        ResponseStatus,         // Accumulate status code digits (e.g. "101").
        ResponseReasonStart,    // Skip leading spaces before reason phrase.
        ResponseReason,         // Accumulate reason phrase characters (e.g. "Switching Protocols").
        ResponseLF,             // Expect LF after CR that terminates the status line.

        Complete,               // Parsing finished successfully.
    }

    private Type _type;                                          // Request vs Response vs Unknown.
    private StringBuilder _method = new();                        // HTTP method (e.g. "GET", "HEAD") — requests only.
    private StringBuilder _uri = new();                           // Request-target URI — requests only.
    private readonly Dictionary<string, string> _headers = new(); // Parsed headers keyed by lowercased name.
    private readonly Dictionary<string, string> _headerNames = new(); // Original-case header names keyed by lowercased name.
    private string _headerName = "";                              // Lowercased name of the header currently being parsed.
    private int _versionMajor;                                    // Major HTTP version number (the "1" in "HTTP/1.1").
    private int _versionMinor;                                    // Minor HTTP version number (the second "1" in "HTTP/1.1").
    private int _status;                                          // Response status code (e.g. 101) — responses only.
    private string _reason = "";                                  // Response reason phrase (e.g. "Switching Protocols").
    private State _state;                                         // Current position in the state machine.

    internal HttpParser()
    {
        _type = Type.Unknown;
        _versionMajor = 0;
        _versionMinor = 0;
        _status = 0;
        _state = State.Init;
    }

    /// <summary>Checks if the buffer contains a complete HTTP message (terminated by a blank line).</summary>
    /// <returns>The position past the end of the message, or -1 if incomplete.</returns>
    internal static int IsCompleteMessage(byte[] buffer, int begin, int end)
    {
        int p = begin;

        // Skip any leading CR-LF characters.
        while (p < end)
        {
            byte ch = buffer[p];
            if (ch != (byte)'\r' && ch != (byte)'\n')
            {
                break;
            }
            ++p;
        }

        // Look for adjacent CR-LF/CR-LF or LF/LF.
        bool seenFirst = false;
        while (p < end)
        {
            byte ch = buffer[p++];
            if (ch == (byte)'\n')
            {
                if (seenFirst)
                {
                    return p;
                }
                else
                {
                    seenFirst = true;
                }
            }
            else if (ch != (byte)'\r')
            {
                seenFirst = false;
            }
        }

        return -1;
    }

    /// <summary>Incrementally parses HTTP request or response bytes. Can be called repeatedly with successive
    /// chunks of data; the parser resumes from its last state on each call.</summary>
    /// <returns><see langword="true"/> if the complete message (start-line + headers + blank line) has been
    /// parsed; <see langword="false"/> if more data is needed.</returns>
    internal bool Parse(byte[] buffer, int begin, int end)
    {
        int p = begin;     // Current read position in the buffer.
        int start = 0;     // Marks the beginning of a token (header name, value, reason, etc.) within the buffer.
        const char CR = '\r';
        const char LF = '\n';

        if (_state == State.Complete)
        {
            _state = State.Init;
        }

        // Process one byte at a time. Each state either:
        //   - `break`    → advance p to the next byte and loop, or
        //   - `continue` → re-evaluate the current byte in the new state (no advance).
        while (p != end && _state != State.Complete)
        {
            char c = (char)buffer[p];

            switch (_state)
            {
                case State.Init:
                {
                    // Reset all accumulated values for a fresh parse. Uses -1 as sentinel
                    // to distinguish "not yet parsed" from a valid value of 0.
                    _method = new StringBuilder();
                    _uri = new StringBuilder();
                    _versionMajor = -1;
                    _versionMinor = -1;
                    _status = -1;
                    _reason = "";
                    _headers.Clear();
                    _state = State.Type;
                    continue; // Re-evaluate the same byte in the new state.
                }
                case State.Type:
                {
                    // Determine whether this is a request or response. Leading CR/LF are
                    // tolerated (some proxies inject extra line endings between messages).
                    // 'H' is ambiguous — it could be "HTTP/1.1 ..." (response) or "HEAD ..."
                    // (request), so we defer to TypeCheck. Any other character means it's a
                    // request method that doesn't start with 'H' (e.g. GET, POST, PUT).
                    if (c == CR || c == LF)
                    {
                        break;
                    }
                    else if (c == 'H')
                    {
                        _state = State.TypeCheck;
                        break;
                    }
                    else
                    {
                        _state = State.Request;
                        continue;
                    }
                }
                case State.TypeCheck:
                {
                    // Disambiguate the 'H' we saw in the previous state:
                    //   "HT..." → "HTTP/..." → this is a response status line.
                    //   "HE..." → "HEAD"     → this is a request; retroactively record "HE" in _method.
                    if (c == 'T')
                    {
                        _state = State.Response;
                    }
                    else if (c == 'E')
                    {
                        _state = State.Request;
                        _method.Append('H');
                        _method.Append('E');
                    }
                    else
                    {
                        throw new InvalidOperationException("malformed request or response");
                    }
                    break;
                }
                case State.Request:
                {
                    _type = Type.Request;
                    _state = State.RequestMethod;
                    continue;
                }
                case State.RequestMethod:
                {
                    if (c == ' ' || c == CR || c == LF)
                    {
                        _state = State.RequestMethodSP;
                        continue;
                    }
                    _method.Append(c);
                    break;
                }
                case State.RequestMethodSP:
                {
                    if (c == ' ')
                    {
                        break;
                    }
                    else if (c == CR || c == LF)
                    {
                        throw new InvalidOperationException("malformed request");
                    }
                    _state = State.RequestURI;
                    continue;
                }
                case State.RequestURI:
                {
                    if (c == ' ' || c == CR || c == LF)
                    {
                        _state = State.RequestURISP;
                        continue;
                    }
                    _uri.Append(c);
                    break;
                }
                case State.RequestURISP:
                {
                    if (c == ' ')
                    {
                        break;
                    }
                    else if (c == CR || c == LF)
                    {
                        throw new InvalidOperationException("malformed request");
                    }
                    _state = State.Version;
                    continue;
                }
                case State.RequestLF:
                {
                    if (c != LF)
                    {
                        throw new InvalidOperationException("malformed request");
                    }
                    _state = State.HeaderFieldStart;
                    break;
                }
                case State.HeaderFieldStart:
                {
                    // We've already seen a LF to reach this state.
                    // Another CR or LF indicates the end of the header fields.
                    if (c == CR)
                    {
                        _state = State.HeaderFieldEndLF;
                        break;
                    }
                    else if (c == LF)
                    {
                        _state = State.Complete;
                        break;
                    }
                    else if (c == ' ')
                    {
                        // Could be a continuation line.
                        _state = State.HeaderFieldContStart;
                        break;
                    }

                    _state = State.HeaderFieldNameStart;
                    continue;
                }
                case State.HeaderFieldContStart:
                {
                    if (c == ' ')
                    {
                        break;
                    }

                    _state = State.HeaderFieldCont;
                    start = p;
                    continue;
                }
                case State.HeaderFieldCont:
                {
                    // RFC 7230 obs-fold: a continuation line starts with SP and appends to the
                    // previous header's value, separated by a single space.
                    if (c == CR || c == LF)
                    {
                        if (p > start)
                        {
                            if (_headerName.Length == 0)
                            {
                                throw new InvalidOperationException("malformed header");
                            }
                            Debug.Assert(_headers.ContainsKey(_headerName));
                            string s = _headers[_headerName];
                            var newValue = new StringBuilder(s);
                            newValue.Append(' ');
                            for (int i = start; i < p; ++i)
                            {
                                newValue.Append((char)buffer[i]);
                            }
                            _headers[_headerName] = newValue.ToString();
                            _state = c == CR ? State.HeaderFieldLF : State.HeaderFieldStart;
                        }
                        else
                        {
                            // Empty continuation line signals the end of headers.
                            _state = c == CR ? State.HeaderFieldEndLF : State.Complete;
                        }
                    }

                    break;
                }
                case State.HeaderFieldNameStart:
                {
                    Debug.Assert(c != ' ');
                    start = p;
                    _headerName = "";
                    _state = State.HeaderFieldName;
                    continue;
                }
                case State.HeaderFieldName:
                {
                    if (c == ' ' || c == ':')
                    {
                        _state = State.HeaderFieldNameEnd;
                        continue;
                    }
                    else if (c == CR || c == LF)
                    {
                        throw new InvalidOperationException("malformed header");
                    }
                    break;
                }
                case State.HeaderFieldNameEnd:
                {
                    // Extract the header name from buffer[start..p], normalize to lowercase
                    // for case-insensitive lookups, and preserve the original casing separately.
                    if (_headerName.Length == 0)
                    {
                        var str = new StringBuilder();
                        for (int i = start; i < p; ++i)
                        {
                            str.Append((char)buffer[i]);
                        }
                        _headerName = str.ToString().ToLowerInvariant();
                        if (!_headers.ContainsKey(_headerName))
                        {
                            _headers[_headerName] = "";
                            _headerNames[_headerName] = str.ToString();
                        }
                    }

                    if (c == ' ')
                    {
                        break;
                    }
                    else if (c != ':' || p == start)
                    {
                        throw new InvalidOperationException("malformed header");
                    }

                    _state = State.HeaderFieldValueStart;
                    break;
                }
                case State.HeaderFieldValueStart:
                {
                    if (c == ' ')
                    {
                        break;
                    }

                    // Check for "Name:\r\n"
                    if (c == CR)
                    {
                        _state = State.HeaderFieldLF;
                        break;
                    }
                    else if (c == LF)
                    {
                        _state = State.HeaderFieldStart;
                        break;
                    }

                    start = p;
                    _state = State.HeaderFieldValue;
                    continue;
                }
                case State.HeaderFieldValue:
                {
                    if (c == CR || c == LF)
                    {
                        _state = State.HeaderFieldValueEnd;
                        continue;
                    }
                    break;
                }
                case State.HeaderFieldValueEnd:
                {
                    // Extract value from buffer[start..p]. If a value already exists for this
                    // header (duplicate header), concatenate with ", " per RFC 7230 §3.2.2.
                    Debug.Assert(c == CR || c == LF);
                    if (p > start)
                    {
                        var str = new StringBuilder();
                        for (int i = start; i < p; ++i)
                        {
                            str.Append((char)buffer[i]);
                        }
                        if (!_headers.TryGetValue(_headerName, out string? s) || s.Length == 0)
                        {
                            _headers[_headerName] = str.ToString();
                        }
                        else
                        {
                            _headers[_headerName] = s + ", " + str.ToString();
                        }
                    }

                    if (c == CR)
                    {
                        _state = State.HeaderFieldLF;
                    }
                    else
                    {
                        _state = State.HeaderFieldStart;
                    }
                    break;
                }
                case State.HeaderFieldLF:
                {
                    if (c != LF)
                    {
                        throw new InvalidOperationException("malformed header");
                    }
                    _state = State.HeaderFieldStart;
                    break;
                }
                case State.HeaderFieldEndLF:
                {
                    if (c != LF)
                    {
                        throw new InvalidOperationException("malformed header");
                    }
                    _state = State.Complete;
                    break;
                }
                case State.Version:
                {
                    if (c != 'H')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    _state = State.VersionH;
                    break;
                }
                case State.VersionH:
                {
                    if (c != 'T')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    _state = State.VersionHT;
                    break;
                }
                case State.VersionHT:
                {
                    if (c != 'T')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    _state = State.VersionHTT;
                    break;
                }
                case State.VersionHTT:
                {
                    if (c != 'P')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    _state = State.VersionHTTP;
                    break;
                }
                case State.VersionHTTP:
                {
                    if (c != '/')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    _state = State.VersionMajor;
                    break;
                }
                case State.VersionMajor:
                {
                    // Parse digits of the major version number (before the '.').
                    if (c == '.')
                    {
                        if (_versionMajor == -1)
                        {
                            throw new InvalidOperationException("malformed version");
                        }
                        _state = State.VersionMinor;
                        break;
                    }
                    else if (c < '0' || c > '9')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    if (_versionMajor == -1)
                    {
                        _versionMajor = 0;
                    }
                    _versionMajor *= 10;
                    _versionMajor += c - '0';
                    break;
                }
                case State.VersionMinor:
                {
                    // Parse digits of the minor version number (after the '.').
                    // After the version is complete, the next transition depends on message type:
                    //   - Request:  CR/LF ends the request line → proceed to headers.
                    //   - Response: SP separates version from the status code.
                    if (c == CR)
                    {
                        if (_versionMinor == -1 || _type != Type.Request)
                        {
                            throw new InvalidOperationException("malformed version");
                        }
                        _state = State.RequestLF;
                        break;
                    }
                    else if (c == LF)
                    {
                        if (_versionMinor == -1 || _type != Type.Request)
                        {
                            throw new InvalidOperationException("malformed version");
                        }
                        _state = State.HeaderFieldStart;
                        break;
                    }
                    else if (c == ' ')
                    {
                        if (_versionMinor == -1 || _type != Type.Response)
                        {
                            throw new InvalidOperationException("malformed version");
                        }
                        _state = State.ResponseVersionSP;
                        break;
                    }
                    else if (c < '0' || c > '9')
                    {
                        throw new InvalidOperationException("malformed version");
                    }
                    if (_versionMinor == -1)
                    {
                        _versionMinor = 0;
                    }
                    _versionMinor *= 10;
                    _versionMinor += c - '0';
                    break;
                }
                case State.Response:
                {
                    _type = Type.Response;
                    _state = State.VersionHT;
                    continue;
                }
                case State.ResponseVersionSP:
                {
                    if (c == ' ')
                    {
                        break;
                    }

                    _state = State.ResponseStatus;
                    continue;
                }
                case State.ResponseStatus:
                {
                    // Accumulate the 3-digit status code (e.g. "101"). After the digits:
                    //   SP → reason phrase follows.   CR/LF → no reason phrase, go to headers.
                    if (c == CR)
                    {
                        if (_status == -1)
                        {
                            throw new InvalidOperationException("malformed response status");
                        }
                        _state = State.ResponseLF;
                        break;
                    }
                    else if (c == LF)
                    {
                        if (_status == -1)
                        {
                            throw new InvalidOperationException("malformed response status");
                        }
                        _state = State.HeaderFieldStart;
                        break;
                    }
                    else if (c == ' ')
                    {
                        if (_status == -1)
                        {
                            throw new InvalidOperationException("malformed response status");
                        }
                        _state = State.ResponseReasonStart;
                        break;
                    }
                    else if (c < '0' || c > '9')
                    {
                        throw new InvalidOperationException("malformed response status");
                    }
                    if (_status == -1)
                    {
                        _status = 0;
                    }
                    _status *= 10;
                    _status += c - '0';
                    break;
                }
                case State.ResponseReasonStart:
                {
                    // Skip leading spaces.
                    if (c == ' ')
                    {
                        break;
                    }

                    _state = State.ResponseReason;
                    start = p;
                    continue;
                }
                case State.ResponseReason:
                {
                    if (c == CR || c == LF)
                    {
                        if (p > start)
                        {
                            var str = new StringBuilder();
                            for (int i = start; i < p; ++i)
                            {
                                str.Append((char)buffer[i]);
                            }
                            _reason = str.ToString();
                        }
                        _state = c == CR ? State.ResponseLF : State.HeaderFieldStart;
                    }

                    break;
                }
                case State.ResponseLF:
                {
                    if (c != LF)
                    {
                        throw new InvalidOperationException("malformed status line");
                    }
                    _state = State.HeaderFieldStart;
                    break;
                }
                case State.Complete:
                {
                    Debug.Assert(false); // Shouldn't reach
                    break;
                }
            }

            ++p;
        }

        return _state == State.Complete;
    }

    // -- Accessors for parsed message fields (valid after Parse returns true) --

    /// <summary>Gets whether the parsed message is a request or response.</summary>
    internal Type MessageType => _type;

    /// <summary>Gets the HTTP method (e.g. "GET"). Only valid for request messages.</summary>
    internal string Method
    {
        get
        {
            Debug.Assert(_type == Type.Request);
            return _method.ToString();
        }
    }

    /// <summary>Gets the request-target URI (e.g. "/"). Only valid for request messages.</summary>
    internal string Uri
    {
        get
        {
            Debug.Assert(_type == Type.Request);
            return _uri.ToString();
        }
    }

    /// <summary>Gets the major HTTP version number (e.g. 1 for HTTP/1.1).</summary>
    internal int VersionMajor => _versionMajor;

    /// <summary>Gets the minor HTTP version number (e.g. 1 for HTTP/1.1).</summary>
    internal int VersionMinor => _versionMinor;

    /// <summary>Gets the response status code (e.g. 101). Only valid for response messages.</summary>
    internal int Status => _status;

    /// <summary>Gets the response reason phrase (e.g. "Switching Protocols"). Only valid for response
    /// messages.</summary>
    internal string Reason => _reason;

    /// <summary>Looks up a parsed header by name (case-insensitive).</summary>
    /// <param name="name">The header name to look up.</param>
    /// <param name="toLower">If <see langword="true"/>, the returned value is lowercased.</param>
    /// <returns>The trimmed header value, or <see langword="null"/> if the header was not present.</returns>
    internal string? GetHeader(string name, bool toLower)
    {
        if (_headers.TryGetValue(name.ToLowerInvariant(), out string? s))
        {
            return toLower ? s.Trim().ToLowerInvariant() : s.Trim();
        }
        return null;
    }
}
