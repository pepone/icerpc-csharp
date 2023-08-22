// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.Protobuf.Generators.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateOperationNames { get; } = new DiagnosticDescriptor(
        id: "PROTO1001",
        title: "Multiple RPC operations cannot use the same name within a service class",
        messageFormat: "Multiple RPC operations are using name {0} in class {1}",
        category: "ProtobufServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
