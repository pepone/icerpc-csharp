// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf;

/// <summary>Represents an attribute used to mark classes implementing Protobuf services.</summary>
/// <remarks>The Protobuf source generator implements <see cref="IDispatcher"/> for classes marked with this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProtobufServiceAttribute : Attribute
{
}
