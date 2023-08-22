// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;

namespace IceRpc.Protoc;

public static class FileDescriptorExtensions
{ 
    public static string GetCsharpNamespace(this FileDescriptor descriptor)
    {
        Google.Protobuf.Reflection.FileOptions fileOptions = descriptor.GetOptions();
        return fileOptions.HasCsharpNamespace ? fileOptions.CsharpNamespace : descriptor.Package;
    }
}
