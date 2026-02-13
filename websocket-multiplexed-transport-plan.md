# WebSocket Multiplexed Transport Plan (IceRPC C#)

## Executive Recommendation

Implement WebSocket support as a **duplex transport** (`IDuplexClientTransport` / `IDuplexServerTransport`) and then
compose it with existing Slic transports:

- `new SlicClientTransport(new WebSocketClientTransport(...))`
- `new SlicServerTransport(new WebSocketServerTransport(...))`

This is the lowest-risk path to support `IMultiplexedClientTransport` and `IMultiplexedServerTransport` over
WebSocket while reusing mature multiplexing logic already in Slic.

## Why This Approach

`IMultiplexedConnection` requires stream creation/accept, stream IDs, flow-control semantics, stream close semantics,
and connection close error mapping. Slic already implements this over `IDuplexConnection`.

Building a direct WebSocket multiplexed transport would duplicate most of Slic internals and create a second
multiplexing stack to maintain.

## Options Comparison

| Option | Summary | Pros | Cons | Recommendation |
| --- | --- | --- | --- | --- |
| A | WebSocket duplex + Slic multiplexing | Reuses existing multiplexing implementation and tests; fastest path; smallest behavioral risk | Slight extra framing overhead (Slic over WS) | **Recommended for v1** |
| B | Native direct `IMultiplexed*Transport` over one WebSocket | Potentially lower overhead | Reimplements stream framing, IDs, credits, close/error mapping, and conformance edge cases | Defer unless proven performance need |

## Proposed Architecture

### 1) New transport package

Add a new package/project (recommended separate from `IceRpc` core):

- `IceRpc.Transports.WebSocket`

Primary types:

- `WebSocketClientTransport : IDuplexClientTransport`
- `WebSocketServerTransport : IDuplexServerTransport`
- `WebSocketClientTransportOptions`
- `WebSocketServerTransportOptions`
- `WebSocketConnection : IDuplexConnection` (internal)
- `WebSocketListener : IListener<IDuplexConnection>` (internal)

Internal implementation types (adapted from Ice):

- `HttpParser` (internal) — incremental HTTP/1.1 request/response parser, adapted from
  [Ice's HttpParser](https://github.com/zeroc-ice/ice/blob/main/csharp/src/Ice/Internal/HttpParser.cs)
- `WebSocketFrame` (internal) — RFC 6455 frame read/write logic, adapted from
  [Ice's WSTransceiver](https://github.com/zeroc-ice/ice/blob/main/csharp/src/Ice/Internal/WSTransceiver.cs)

### 2) Multiplexing composition

No new native multiplexed transport type is required for v1. Multiplexed usage is:

- client: `new SlicClientTransport(slicOptions, new WebSocketClientTransport(wsClientOptions))`
- server: `new SlicServerTransport(slicOptions, new WebSocketServerTransport(wsServerOptions))`

### 3) Hosting model (server side)

Implement the server transport using `TcpListener` + `SslStream` + custom HTTP upgrade handshake + RFC 6455 framing.
This mirrors the architecture of the existing TCP transport (`TcpServerTransport` / `TcpAcceptor`) and reuses proven
WebSocket handshake and framing logic from the
[Ice C# WebSocket implementation](https://github.com/zeroc-ice/ice/tree/main/csharp/src/Ice/Internal).

Rationale:

- **Zero external dependencies** — no ASP.NET Core, no Kestrel, no `HttpListener`. The package depends only on the
  .NET runtime (`System.Net.Sockets`, `System.Net.Security`, `System.Net.WebSockets`).
- **Architectural consistency** — the server transport follows the same `TcpListener` + `SslStream` pattern already
  used by `TcpServerTransport`, making it familiar and reviewable.
- **Native TLS integration** — TLS is configured through `SslServerAuthenticationOptions` /
  `SslClientAuthenticationOptions`, matching the existing `IDuplexServerTransport.Listen()` /
  `IDuplexClientTransport.CreateConnection()` signatures with no impedance mismatch.
- **Cross-platform** — `TcpListener` and `SslStream` work on Linux, macOS, and Windows.
- **No middleware lifetime management** — avoids the ASP.NET Core constraint of keeping the HTTP request pipeline alive
  for the WebSocket connection's duration.

### 4) Reuse from Ice WebSocket implementation

The [Ice C# codebase](https://github.com/zeroc-ice/ice/tree/main/csharp/src/Ice/Internal) contains a complete,
battle-tested WebSocket implementation over raw TCP sockets. The following components are adapted for IceRPC:

#### `HttpParser` — HTTP/1.1 parser (lift with minimal changes)

Source: `Ice.Internal.HttpParser` (~450 lines)

- Self-contained incremental state-machine parser for HTTP/1.1 requests and responses.
- Handles method, URI, version, status code, reason phrase, and headers (including continuation lines).
- No dependencies on Ice runtime types beyond `ByteBuffer` (replaced with `ReadOnlySpan<byte>` or `Memory<byte>`).
- Reusable nearly verbatim.

Adaptation required:

- Replace `ByteBuffer` parameter types with `ReadOnlySpan<byte>` or a buffer abstraction compatible with
  `System.IO.Pipelines`.
- Replace `WebSocketException` with `IceRpcException` or a transport-specific exception.
- Make the API async-friendly (the parser itself is synchronous; the caller feeds bytes incrementally).

#### `WSTransceiver` handshake logic — RFC 6455 upgrade (extract and simplify)

Source: `Ice.Internal.WSTransceiver.handleRequest()` (~125 lines) and `handleResponse()` (~100 lines)

Client handshake (`handleResponse` equivalent):

- Compose `GET <path> HTTP/1.1` with required headers (`Host`, `Upgrade: websocket`, `Connection: Upgrade`,
  `Sec-WebSocket-Version: 13`, `Sec-WebSocket-Key: <random-base64>`).
- Validate server's `101 Switching Protocols` response.
- Verify `Sec-WebSocket-Accept` header (SHA-1 hash of key + RFC 6455 UUID).

Server handshake (`handleRequest` equivalent):

- Parse incoming `GET` request, validate `Upgrade`, `Connection`, `Sec-WebSocket-Version`, `Sec-WebSocket-Key`.
- Compose `101 Switching Protocols` response with `Sec-WebSocket-Accept`.

Adaptation required:

- Replace Ice buffer types with `Memory<byte>` / byte arrays.
- Replace `ice.zeroc.com` subprotocol with an IceRPC-specific subprotocol or omit.
- Wire into `ConnectAsync` flow (client sends request + reads response; server reads request + sends response).

#### `WSTransceiver` framing logic — RFC 6455 frame read/write (adapt to async model)

Source: `Ice.Internal.WSTransceiver` `preRead`/`postRead`/`preWrite`/`postWrite`/`prepareWriteHeader` (~600 lines)

Covers the full RFC 6455 data framing specification:

- **Opcodes**: binary data (0x2), continuation (0x0), close (0x8), ping (0x9), pong (0xA).
- **Payload length encoding**: 7-bit (0–125), 16-bit (126), 64-bit (127).
- **Masking**: client-to-server frames are masked with a random 32-bit key; server-to-client frames are unmasked.
- **FIN bit**: tracks fragmentation across continuation frames.
- **Control frames**: ping/pong handling, close frame exchange with reason codes.

Adaptation required:

- Replace Ice's synchronous callback model (`startRead`/`finishRead`, `SocketOperation` flags) with
  `async Task`/`ValueTask` methods matching `IDuplexConnection`.
- Replace Ice's `Buffer`/`ByteBuffer` with `Memory<byte>`, `ReadOnlySequence<byte>`, and `MemoryPool<byte>`.
- Extract framing into a focused class (`WebSocketFrame` or similar) separate from connection lifecycle.
- Map close reason codes to `IceRpcException` error codes.

## Address and Configuration Design

### Transport name

Use transport name `ws` for IceRPC `ServerAddress.Transport` matching current transport naming patterns.

### Server address parameters

Because `ServerAddress` currently rejects non-empty URI paths, carry WebSocket endpoint path through transport params.

Recommended supported params:

- `path`: WebSocket endpoint path, default `/icerpc`

Example:

- `icerpc://example.com:8080?transport=ws&path=%2Ficerpc`

Unknown params should fail fast with `ArgumentException` (conformance behavior).

### TLS behavior

Follow existing transport patterns:

- Client TLS configured through `SslClientAuthenticationOptions`.
- Server TLS configured through `SslServerAuthenticationOptions`.
- If server auth options are present, the server wraps the `NetworkStream` with `SslStream` before the HTTP upgrade
  handshake (TLS-first, same as the TCP/SSL transport).

## Duplex Contract Mapping (Critical)

`WebSocketConnection` must satisfy `IDuplexConnection` semantics. The connection wraps a `TcpClient`/`Socket` with
optional `SslStream`, performs the HTTP upgrade handshake, then uses RFC 6455 binary framing for data transfer.

- `ConnectAsync`:
  - Client: open TCP socket → optional TLS handshake via `SslStream.AuthenticateAsClientAsync` → send HTTP upgrade
    request → read and validate `101` response → return `TransportConnectionInformation` (with `IPEndPoint` from
    the socket).
  - Server: the connection is accepted by the listener, which handles TCP accept → optional TLS → read HTTP upgrade
    request → send `101` response. `ConnectAsync` on the server side completes this flow and returns
    `TransportConnectionInformation`.
- `ReadAsync`: read RFC 6455 binary frames from the underlying stream, unmask if needed, strip frame headers, and
  return payload bytes as a continuous byte stream. Returns `0` when a close frame is received.
- `WriteAsync`: wrap `ReadOnlySequence<byte>` payload in RFC 6455 binary frame(s) with proper header and masking
  (client-to-server), then write to the underlying stream.
- `ShutdownWriteAsync`: send a RFC 6455 close frame (opcode 0x8) with normal closure reason code. Disallow further
  writes while allowing reads to continue until the peer's close frame is received.
- `Dispose`: close the underlying socket/stream promptly and unblock any pending read/write/connect operations.

Implementation notes:

- Treat WebSocket message boundaries as transport-internal; expose a continuous byte stream to Slic.
- Handle fragmentation and partial receives without losing bytes.
- Ensure cancellation of read/write/connect propagates as expected via `CancellationToken`.
- Map transport failures to `IceRpcException` with consistent error codes.
- Respond to incoming ping frames with pong frames transparently (handled within `ReadAsync`).

## Phased Implementation Plan

### Phase 0: Design decisions

- Confirm `TcpListener` + `SslStream` + Ice-derived WS code approach (this plan).
- Finalize supported server address params (`path` only for v1).
- Finalize package naming and dependencies.
- Decide on WebSocket subprotocol string (e.g., `icerpc` or none).

Deliverable: short design decision record added to repo.

### Phase 1: Project scaffolding

- Create `src/IceRpc.Transports.WebSocket/`.
- Add project to `IceRpc.slnx`.
- Add package metadata and API namespace docs.
- Project should target the same TFMs as the core IceRpc package.

Deliverable: buildable package skeleton.

### Phase 2: Port Ice WebSocket internals

Adapt the following from Ice's C# codebase into `IceRpc.Transports.WebSocket/Internal/`:

- **`HttpParser`**: replace `ByteBuffer` with `ReadOnlySpan<byte>` / `Memory<byte>`. Remove Ice-specific exception
  types. Keep the incremental state-machine design.
- **WebSocket frame reader/writer** (from `WSTransceiver`): extract `prepareWriteHeader`, `preRead`/`postRead` frame
  parsing, and masking logic into a dedicated `WebSocketFrameHelper` or similar. Replace Ice's `Buffer` type with
  `Memory<byte>` and `MemoryPool<byte>`. Convert to async-compatible design.
- **Handshake logic** (from `WSTransceiver.handleRequest`/`handleResponse`): extract into helper methods usable from
  `ConnectAsync`. Replace Ice buffer types. Replace `ice.zeroc.com` subprotocol reference.

Deliverable: internal classes with unit tests for HTTP parsing, frame encoding/decoding, and handshake validation.

### Phase 3: Duplex client and server transport

- Implement `WebSocketClientTransport` + `WebSocketClientConnection`:
  - `CreateConnection` opens a `Socket`, optionally wraps in `SslStream`, returns a `WebSocketClientConnection`.
  - `ConnectAsync` sends HTTP upgrade request, validates response, returns `TransportConnectionInformation`.
  - `ReadAsync`/`WriteAsync` use the ported frame reader/writer over the `NetworkStream`/`SslStream`.
- Implement `WebSocketServerTransport` + `WebSocketListener` + `WebSocketServerConnection`:
  - `Listen` creates a `TcpListener`, optionally configured for TLS.
  - `AcceptAsync` accepts TCP connections, optionally wraps in `SslStream`, reads the HTTP upgrade request, sends
    `101` response, returns a `WebSocketServerConnection`.
  - Read/write implementation shared with the client connection (or via a common base/helper).
- Implement server address validation and `path` parameter extraction.
- Implement `TransportConnectionInformation` construction using `IPEndPoint` from the underlying socket and optional
  `RemoteCertificate` from `SslStream`.

Deliverable: end-to-end duplex connectivity over WebSocket.

### Phase 4: Multiplexed composition with Slic

- Wire examples/tests to use `SlicClientTransport(WebSocketClientTransport)` and
  `SlicServerTransport(WebSocketServerTransport)`.
- Optionally add helper factory/DI methods for common setup.

Deliverable: working multiplexed transport path over WebSocket.

### Phase 5: Conformance and transport tests

Add test suites analogous to existing TCP/Slic/QUIC tests:

- Duplex conformance for WebSocket transport.
- Multiplexed conformance for Slic-over-WebSocket.
- SSL/TLS authentication tests.
- Idle/keep-alive and close-handshake regression tests.
- Parameter validation tests (`unknown-parameter` must fail).
- WebSocket-specific tests: ping/pong handling, fragmented frames, masking correctness, close frame exchange.

Deliverable: passing conformance matrix for duplex and multiplexed scenarios.

### Phase 6: Documentation and samples

- Add usage docs with client/server setup examples.
- Add example showing `Slic + WebSocket`.
- Document `path` parameter, TLS configuration, and proxy expectations.

Deliverable: docfx + README updates and a runnable sample.

## Recommended API Additions

Minimal v1 options:

- `WebSocketClientTransportOptions`
  - `Path` (default `/icerpc`)
  - `LocalNetworkAddress` (`IPEndPoint?`) — optional local bind address, matching `TcpClientTransportOptions`
- `WebSocketServerTransportOptions`
  - `Path` (default `/icerpc`)
  - `ListenBacklog` (default `511`) — matching `TcpServerTransportOptions`
- Both options classes may optionally expose:
  - `ReceiveBufferSize` / `SendBufferSize` — socket buffer tuning, matching `TcpTransportOptions`

Note: WebSocket-level keep-alive (ping/pong) can be added in a follow-up. For v1, Slic's `IdleTimeout` provides
connection liveness detection at the multiplexing layer.

## Risks and Mitigations

### Risk: half-close mismatch between WebSocket and duplex semantics

WebSocket close is a two-phase handshake (close frame → close response), not a TCP-style half-close.
`ShutdownWriteAsync` must send a close frame while still allowing `ReadAsync` to receive data until the peer's close
frame arrives.

Mitigation: strict state machine around `ShutdownWriteAsync`, read EOF handling, and close handshake sequencing.
The Ice `WSTransceiver` already implements this state machine (states `StateClosingRequestPending` /
`StateClosingResponsePending`), providing a proven reference.

### Risk: server path not represented in `ServerAddress` path

Mitigation: use `path` transport parameter for v1; keep `ServerAddress` model unchanged initially.

### Risk: adaptation effort from Ice's async model

Ice uses a callback-based async model (`startRead`/`finishRead`, `SocketOperation` flags) while IceRPC uses
`async`/`await` with `ValueTask<int>` / `Task`. The framing logic must be restructured.

Mitigation: extract the stateless parts (frame header encoding/decoding, HTTP parsing, masking) as pure helper
methods. Only the read/write orchestration needs to be rewritten for the `async`/`await` model. The protocol logic
itself (opcodes, payload lengths, mask application, close handshake) remains identical.

### Risk: proxy/load-balancer idle timeouts

Mitigation: expose keep-alive tuning in a follow-up and document recommended proxy settings. For v1, Slic's
`IdleTimeout` provides a baseline.

### Risk: RFC 6455 compliance gaps

Mitigation: the Ice WebSocket implementation has been in production for years and covers the full RFC 6455 frame
specification including masking, fragmentation, control frames, and close handshake. Porting it preserves this
coverage. Additionally, the conformance test suite (Phase 5) validates behavior end-to-end.

## Recommendation on Direct Native Multiplexing

Do **not** implement direct `IMultiplexedConnection` over WebSocket in v1.

Revisit only if measurements show Slic-over-WebSocket overhead is a material issue for target workloads. If revisited,
budget additional work for:

- stream ID allocator compatible with current semantics
- stream and connection flow-control windows
- stream semaphore behavior matching conformance tests
- close/error code mapping parity with existing transports
- full conformance parity and long-term maintenance

## Acceptance Criteria

The effort is complete when:

- WebSocket duplex transport passes duplex transport conformance tests.
- Slic-over-WebSocket passes multiplexed connection/stream/listener conformance tests.
- TLS and close/cancellation behaviors are validated with dedicated tests.
- WebSocket framing internals (HTTP parser, frame encoding/decoding, masking) have unit test coverage.
- Docs and sample usage are published and reviewed.
