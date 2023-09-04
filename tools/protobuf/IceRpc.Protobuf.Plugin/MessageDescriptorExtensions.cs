// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protoc;

internal static class MessageDescriptorExtensions
{
    internal static string GetFullyQualifiedType(this MessageDescriptor messageDescriptor) =>
        $"global::{messageDescriptor.File.GetCsharpNamespace()}.{messageDescriptor.Name}";
}
