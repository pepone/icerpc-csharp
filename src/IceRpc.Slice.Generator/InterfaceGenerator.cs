// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using static ZeroC.Slice.Generator.OperationHelpers;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# client proxy, service interface, and encoder/decoder extensions from a Slice interface
/// definition.</summary>
internal static class InterfaceGenerator
{
    internal static CodeBlock Generate(Interface interfaceDef)
    {
        string name = interfaceDef.Name;
        string scopedId = interfaceDef.ScopedIdentifier;
        string accessModifier = interfaceDef.AccessModifier;
        string currentNamespace = interfaceDef.Namespace;
        string defaultServicePath = $"/{currentNamespace}.{name}";

        return CodeBlock.FromBlocks(
        [
            GenerateClientInterface(interfaceDef, name, scopedId, accessModifier, currentNamespace),
            GenerateProxyStruct(interfaceDef, name, scopedId, accessModifier, currentNamespace, defaultServicePath),
            GenerateProxyEncoderExtensions(name, accessModifier),
            GenerateProxyDecoderExtensions(name, accessModifier),
            GenerateServiceInterface(interfaceDef, name, scopedId, accessModifier, currentNamespace, defaultServicePath),
        ]);
    }

    // --- Client Interface ---

    private static CodeBlock GenerateClientInterface(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace)
    {
        var builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}");
        builder.AddComment(
            "remarks",
            $"""The Slice compiler generated this client-side interface from Slice interface <c>{scopedId}</c>.{"\n"}It's implemented by <see cref="{name}Proxy" />.""");

        foreach (Operation op in interfaceDef.Operations)
        {
            if (!IsNonStreaming(op))
            {
                continue;
            }

            builder.AddBlock(BuildClientOperationDeclaration(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildClientOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = GetClientReturnType(op, currentNamespace);
        ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);

        var fn = new FunctionBuilder("", returnType, $"{opName}Async", FunctionType.Declaration);

        // Operation parameters (outgoing type for proxy)
        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        fn.AddParameter("IceRpc.Features.IFeatureCollection?", "features", "null", "The invocation features.");
        fn.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            "default",
            "A cancellation token that receives the cancellation requests.");

        if (GetNonStreamedReturns(op).Count == 0)
        {
            fn.AddComment("returns", "A task that completes when the response is received.");
        }

        return fn.Build();
    }

    // --- Proxy Struct ---

    private static CodeBlock GenerateProxyStruct(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace,
        string defaultServicePath)
    {
        string proxyName = $"{name}Proxy";

        var builder = new ContainerBuilder($"{accessModifier} readonly partial record struct", proxyName);
        builder.AddComment(
            "summary",
            $"""Implements <see cref="I{name}" /> by making invocations on a remote IceRPC service.{"\n"}This remote service must implement Slice interface {scopedId}.""");
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this record struct from the Slice interface <c>{scopedId}</c>.");
        builder.AddBase($"I{name}");
        builder.AddBase("ISliceProxy");

        builder.AddBlock(BuildProxyRequestClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildProxyResponseClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildProxyProperties(proxyName, scopedId, defaultServicePath));
        builder.AddBlock(BuildProxyConstructors(proxyName, defaultServicePath));

        foreach (Operation op in interfaceDef.Operations)
        {
            if (IsNonStreaming(op))
            {
                builder.AddBlock(BuildProxyOperationImpl(op, currentNamespace));
            }
        }

        return builder.Build();
    }

    private static CodeBlock BuildProxyRequestClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var request = new ContainerBuilder("public static class", "Request");
        request.AddComment("summary", "Provides static methods that encode operation arguments into request payloads.");
        request.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            if (!IsNonStreaming(op))
            {
                continue;
            }

            ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);
            string opName = op.Name;

            CodeBlock? encodeBody = GenerateEncodeBody(nonStreamedParams, currentNamespace);

            if (encodeBody is null)
            {
                // Void encode — no parameters
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Encodes the arguments of operation <c>{op.Identifier}</c> into a request payload.");
                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "The Slice-encoded payload.");
                fn.SetBody(new CodeBlock("IceRpc.EmptyPipeReader.Instance"));
                request.AddBlock(fn.Build());
            }
            else
            {
                // Parameters present — pipe encode
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody);
                fn.AddComment(
                    "summary",
                    $"Encodes the argument{(nonStreamedParams.Count > 1 ? "s" : "")} of operation <c>{op.Identifier}</c> into a request payload.");

                foreach (Field param in nonStreamedParams)
                {
                    fn.AddParameter(
                        param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                        param.ParameterName);
                }

                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "The Slice-encoded payload.");

                var body = new CodeBlock($$"""
                    var pipe_ = new global::System.IO.Pipelines.Pipe(
                        encodeOptions?.PipeOptions ?? SliceEncodeOptions.Default.PipeOptions);
                    var encoder = new SliceEncoder(pipe_.Writer);

                    Span<byte> sizePlaceholder_ = encoder.GetPlaceholderSpan(4);
                    int startPos_ = encoder.EncodedByteCount;

                    """);
                body.AddBlock(encodeBody);
                body.WriteLine("""

                    SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos_), sizePlaceholder_);

                    pipe_.Writer.Complete();
                    return pipe_.Reader;
                    """);
                fn.SetBody(body);
                request.AddBlock(fn.Build());
            }
        }

        return request.Build();
    }

    private static CodeBlock BuildProxyResponseClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var response = new ContainerBuilder("public static class", "Response");
        response.AddComment(
            "summary",
            "Provides a <see cref=\"ResponseDecodeFunc{T}\" /> for each operation defined in Slice interface " + scopedId + ".");
        response.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            if (!IsNonStreaming(op))
            {
                continue;
            }

            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = GetNonStreamedReturns(op);
            string returnType = GetProxyResponseReturnType(op, currentNamespace);

            var fn = new FunctionBuilder(
                "public static",
                returnType,
                $"Decode{opName}Async",
                FunctionType.ExpressionBody);
            fn.AddComment("summary", $"Decodes an incoming response for operation <c>{op.Identifier}</c>.");
            fn.AddParameter("IceRpc.IncomingResponse", "response");
            fn.AddParameter("IceRpc.OutgoingRequest", "request");
            fn.AddParameter("ISliceProxy", "sender");
            fn.AddParameter("global::System.Threading.CancellationToken", "cancellationToken");

            if (nonStreamedReturns.Count == 0)
            {
                fn.SetBody(new CodeBlock("""
                    response.DecodeVoidReturnValueAsync(
                        request,
                        cancellationToken)
                    """));
            }
            else
            {
                string decodeLambda = GenerateDecodeLambda(nonStreamedReturns, currentNamespace);
                fn.SetBody(new CodeBlock($$"""
                    response.DecodeReturnValueAsync(
                        request,
                        sender,
                        {{decodeLambda}},
                        cancellationToken)
                    """));
            }

            response.AddBlock(fn.Build());
        }

        return response.Build();
    }

    private static CodeBlock BuildProxyOperationImpl(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = GetClientReturnType(op, currentNamespace);
        ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);

        var fn = new FunctionBuilder("public", returnType, $"{opName}Async", FunctionType.ExpressionBody);
        fn.SetInheritDoc(true);

        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        fn.AddParameter("IceRpc.Features.IFeatureCollection?", "features", "null");
        fn.AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default");

        string encodeArgs = nonStreamedParams.Count > 0
            ? string.Join(", ", nonStreamedParams.Select(p => p.ParameterName)) + ", encodeOptions: EncodeOptions"
            : "encodeOptions: EncodeOptions";

        var bodyBuilder = new FunctionCallBuilder($"this.InvokeOperationAsync");
        bodyBuilder.ArgumentsOnNewLine(true);
        bodyBuilder.UseSemicolon(false);
        bodyBuilder.AddArgument($"\"{op.Identifier}\"");
        bodyBuilder.AddArgument($"payload: Request.Encode{opName}({encodeArgs})");
        bodyBuilder.AddArgument("payloadContinuation: null");
        bodyBuilder.AddArgument($"Response.Decode{opName}Async");
        bodyBuilder.AddArgument("features");
        if (op.IsIdempotent)
        {
            bodyBuilder.AddArgument("isIdempotent: true");
        }
        bodyBuilder.AddArgument("cancellationToken: cancellationToken");

        fn.SetBody(bodyBuilder.Build());
        return fn.Build();
    }

    // --- Proxy Encoder/Decoder Extensions ---

    private static CodeBlock GenerateProxyEncoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        var container = new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceEncoderExtensions");
        container.AddComment(
            "summary",
            $"""Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="{proxyName}" />.""");

        var fn = new FunctionBuilder("public static", "void", $"Encode{proxyName}", FunctionType.ExpressionBody);
        fn.AddComment("summary", $"""Encodes a <see cref="{proxyName}" /> as an <see cref="IceRpc.ServiceAddress" />.""");
        fn.AddParameter("this ref SliceEncoder", "encoder", docComment: "The Slice encoder.");
        fn.AddParameter(proxyName, "proxy", docComment: "The proxy to encode as a service address.");
        fn.SetBody(new CodeBlock("encoder.EncodeServiceAddress(proxy.ServiceAddress)"));

        container.AddBlock(fn.Build());
        return container.Build();
    }

    private static CodeBlock GenerateProxyDecoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        var container = new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceDecoderExtensions");
        container.AddComment(
            "summary",
            $"""Provides an extension method for <see cref="SliceDecoder" /> to decode a <see cref="{proxyName}" />.""");

        var fn = new FunctionBuilder("public static", proxyName, $"Decode{proxyName}", FunctionType.ExpressionBody);
        fn.AddComment("summary", $"""Decodes an <see cref="IceRpc.ServiceAddress" /> into a <see cref="{proxyName}" />.""");
        fn.AddParameter("this ref SliceDecoder", "decoder", docComment: "The Slice decoder.");
        fn.AddComment("returns", "The proxy created from the decoded service address.");
        fn.SetBody(new CodeBlock($"decoder.DecodeProxy<{proxyName}>()"));

        container.AddBlock(fn.Build());
        return container.Build();
    }

    // --- Service Interface ---

    private static CodeBlock GenerateServiceInterface(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace,
        string defaultServicePath)
    {
        var builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}Service");
        builder.AddComment(
            "remarks",
            $"""The Slice compiler generated this server-side interface from Slice interface <c>{scopedId}</c>.{"\n"}Your service implementation must implement this interface.""");
        builder.AddAttribute($"""IceRpc.DefaultServicePath("{defaultServicePath}")""");

        builder.AddBlock(BuildServiceRequestClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildServiceResponseClass(interfaceDef, scopedId, currentNamespace));

        foreach (Operation op in interfaceDef.Operations)
        {
            if (IsNonStreaming(op))
            {
                builder.AddBlock(BuildServiceOperationDeclaration(op, currentNamespace));
            }
        }

        return builder.Build();
    }

    private static CodeBlock BuildServiceRequestClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var request = new ContainerBuilder("public static class", "Request");
        request.AddComment("summary", "Provides static methods that decode request payloads.");
        request.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            if (!IsNonStreaming(op))
            {
                continue;
            }

            string opName = op.Name;
            ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);
            string returnType = GetServiceRequestReturnType(op, currentNamespace);

            var fn = new FunctionBuilder(
                "public static",
                returnType,
                $"Decode{opName}Async",
                FunctionType.ExpressionBody);
            fn.AddComment("summary", $"Decodes the request payload of operation <c>{op.Identifier}</c>.");
            fn.AddParameter("IceRpc.IncomingRequest", "request", docComment: "The incoming request.");
            fn.AddParameter(
                "global::System.Threading.CancellationToken",
                "cancellationToken",
                docComment: "A cancellation token that receives the cancellation requests.");

            if (nonStreamedParams.Count == 0)
            {
                fn.SetBody(new CodeBlock("request.DecodeEmptyArgsAsync(cancellationToken)"));
            }
            else
            {
                string decodeLambda = GenerateDecodeLambda(nonStreamedParams, currentNamespace);
                fn.SetBody(new CodeBlock($$"""
                    request.DecodeArgsAsync(
                        {{decodeLambda}},
                        cancellationToken)
                    """));
            }

            request.AddBlock(fn.Build());
        }

        return request.Build();
    }

    private static CodeBlock BuildServiceResponseClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var response = new ContainerBuilder("public static class", "Response");
        response.AddComment("summary", "Provides static methods that encode return values into response payloads.");
        response.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            if (!IsNonStreaming(op))
            {
                continue;
            }

            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = GetNonStreamedReturns(op);
            CodeBlock? encodeBody = GenerateEncodeBody(nonStreamedReturns, currentNamespace);

            if (encodeBody is null)
            {
                // Void return — empty response
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.");
                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "A new response payload.");
                fn.SetBody(new CodeBlock("IceRpc.EmptyPipeReader.Instance"));
                response.AddBlock(fn.Build());
            }
            else
            {
                // Return values present — pipe encode
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody);
                fn.AddComment("summary", $"Encodes the return value of operation <c>{op.Identifier}</c> into a response payload.");

                foreach (Field ret in nonStreamedReturns)
                {
                    fn.AddParameter(
                        ret.DataType.FieldTypeString(ret.DataTypeIsOptional, currentNamespace),
                        ret.ParameterName);
                }

                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "A new response payload.");

                var body = new CodeBlock($$"""
                    var pipe_ = new global::System.IO.Pipelines.Pipe(
                        encodeOptions?.PipeOptions ?? SliceEncodeOptions.Default.PipeOptions);
                    var encoder = new SliceEncoder(pipe_.Writer);

                    Span<byte> sizePlaceholder_ = encoder.GetPlaceholderSpan(4);
                    int startPos_ = encoder.EncodedByteCount;

                    """);
                body.AddBlock(encodeBody);
                body.WriteLine("""

                    SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos_), sizePlaceholder_);

                    pipe_.Writer.Complete();
                    return pipe_.Reader;
                    """);
                fn.SetBody(body);
                response.AddBlock(fn.Build());
            }
        }

        return response.Build();
    }

    private static CodeBlock BuildServiceOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);
        string returnType = GetServiceReturnType(op, currentNamespace);

        var fn = new FunctionBuilder("public", returnType, $"{opName}Async", FunctionType.Declaration);

        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.FieldTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        fn.AddParameter("IceRpc.Features.IFeatureCollection", "features", docComment: "The dispatch features.");
        fn.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            docComment: "A cancellation token that receives the cancellation requests.");

        if (GetNonStreamedReturns(op).Count == 0)
        {
            fn.AddComment("returns", "A value task that completes when this implementation completes.");
        }

        fn.AddAttribute($"""SliceOperation("{op.Identifier}")""");
        return fn.Build();
    }

    // --- Proxy Properties and Constructors (unchanged) ---

    private static CodeBlock BuildProxyProperties(string proxyName, string scopedId, string defaultServicePath) =>
        new($$"""
            /// <summary>Represents the default path for IceRPC services that implement Slice interface
            /// <c>{{scopedId}}</c>.</summary>
            public const string DefaultServicePath = "{{defaultServicePath}}";

            /// <inheritdoc/>
            public SliceEncodeOptions? EncodeOptions { get; init; }

            /// <inheritdoc/>
            public required IceRpc.IInvoker Invoker { get; init; }

            /// <inheritdoc/>
            public IceRpc.ServiceAddress ServiceAddress { get; init; } = _defaultServiceAddress;

            private static IceRpc.ServiceAddress _defaultServiceAddress =
                new(IceRpc.Protocol.IceRpc) { Path = DefaultServicePath };
            """);

    private static CodeBlock BuildProxyConstructors(string proxyName, string defaultServicePath)
    {
        var fromPath = new FunctionBuilder("public static", proxyName, "FromPath", FunctionType.ExpressionBody);
        fromPath.AddComment("summary", "Creates a relative proxy from a path.");
        fromPath.AddParameter("string", "path", docComment: "The path.");
        fromPath.AddComment("returns", "The new relative proxy.");
        fromPath.SetBody(new CodeBlock("new(IceRpc.InvalidInvoker.Instance, new IceRpc.ServiceAddress { Path = path })"));

        var mainCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        mainCtor.AddComment("summary", "Constructs a proxy from an invoker, a service address and encode options.");
        mainCtor.AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.");
        mainCtor.AddParameter(
            "IceRpc.ServiceAddress?",
            "serviceAddress",
            "null",
            @$"The service address. <see langword=""null"" /> is equivalent to an icerpc service address{"\n"}with path <see cref=""DefaultServicePath"" />.");
        mainCtor.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The encode options, used to customize the encoding of request payloads.");
        mainCtor.AddSetsRequiredMembersAttribute();
        mainCtor.SetBody(new CodeBlock("""
            Invoker = invoker;
            ServiceAddress = serviceAddress ?? _defaultServiceAddress;
            EncodeOptions = encodeOptions;
            """));

        var uriCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        uriCtor.AddComment("summary", "Constructs a proxy from an invoker, a service address URI and encode options.");
        uriCtor.AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.");
        uriCtor.AddParameter("System.Uri", "serviceAddressUri", docComment: "A URI that represents a service address.");
        uriCtor.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The encode options, used to customize the encoding of request payloads.");
        uriCtor.AddSetsRequiredMembersAttribute();
        uriCtor.AddBaseParameters(["invoker", "new IceRpc.ServiceAddress(serviceAddressUri)", "encodeOptions"]);

        var defaultCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        defaultCtor.AddComment("summary", @$"Constructs a proxy with an icerpc service address with path <see cref=""DefaultServicePath"" />.");

        return CodeBlock.FromBlocks([fromPath.Build(), mainCtor.Build(), uriCtor.Build(), defaultCtor.Build()]);
    }
}
