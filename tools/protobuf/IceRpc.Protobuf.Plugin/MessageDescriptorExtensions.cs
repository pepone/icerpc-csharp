// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protoc;

public static class MessageDescriptorExtensions
{
    public static string GetFullyQualifiedType(this MessageDescriptor messageDescriptor) =>
        $"global::{messageDescriptor.File.GetCsharpNamespace()}.{messageDescriptor.Name}";
}
