# WebSocket transport for IceRPC

IceRpc.Transports.WebSocket is an implementation of [IceRPC][icerpc-csharp]'s duplex transport abstraction that uses
WebSocket (RFC 6455) over TCP. You can use this transport to traverse HTTP infrastructure such as reverse proxies, load
balancers, and firewalls that do not support QUIC or plain TCP.

This transport supports both `ws` (plain) and `wss` (TLS) connections.

[Source code][source] | [Package][package] | [API reference][api] | [Product documentation][product]

## Sample code

```csharp
// Create an IceRPC server with WebSocket

using IceRpc;
using IceRpc.Transports.WebSocket;

await using var server = new Server(
    dispatcher: ...,
    multiplexedServerTransport: new SlicServerTransport(new WebSocketServerTransport()));

server.Listen();

// Create a client connection to this server

await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    multiplexedClientTransport: new SlicClientTransport(new WebSocketClientTransport()));

await connection.ConnectAsync();
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Transports.WebSocket.html
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[package]: https://www.nuget.org/packages/IceRpc.Transports.WebSocket
[product]: https://docs.icerpc.dev/icerpc
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Transports.WebSocket
