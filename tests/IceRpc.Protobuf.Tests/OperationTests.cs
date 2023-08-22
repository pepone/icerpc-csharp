// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class OperationTests
{
    [Test]
    public void Unary_operation_with_empty_param_and_return()
    {
    }
}
