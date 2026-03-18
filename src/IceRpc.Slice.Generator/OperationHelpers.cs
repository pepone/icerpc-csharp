// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

namespace ZeroC.Slice.Generator;

/// <summary>Helper methods for generating operation encode/decode code in the InterfaceGenerator.</summary>
internal static class OperationHelpers
{
    /// <summary>Returns the C# return type for an operation (Task, Task&lt;T&gt;, or Task&lt;tuple&gt;).</summary>
    internal static string GetClientReturnType(Operation op, string currentNamespace)
    {
        ImmutableList<Field> nonStreamedReturns = GetNonStreamedReturns(op);
        return nonStreamedReturns.Count switch
        {
            0 => "global::System.Threading.Tasks.Task",
            1 => $"global::System.Threading.Tasks.Task<{nonStreamedReturns[0].DataType.FieldTypeString(nonStreamedReturns[0].DataTypeIsOptional, currentNamespace)}>",
            _ => $"global::System.Threading.Tasks.Task<({string.Join(", ", nonStreamedReturns.Select(r => $"{r.DataType.FieldTypeString(r.DataTypeIsOptional, currentNamespace)} {r.Name}"))})>",
        };
    }

    /// <summary>Returns the C# return type for a service operation (ValueTask, ValueTask&lt;T&gt;, or
    /// ValueTask&lt;tuple&gt;).</summary>
    internal static string GetServiceReturnType(Operation op, string currentNamespace)
    {
        ImmutableList<Field> nonStreamedReturns = GetNonStreamedReturns(op);
        return nonStreamedReturns.Count switch
        {
            0 => "global::System.Threading.Tasks.ValueTask",
            1 => $"global::System.Threading.Tasks.ValueTask<{nonStreamedReturns[0].DataType.FieldTypeString(nonStreamedReturns[0].DataTypeIsOptional, currentNamespace)}>",
            _ => $"global::System.Threading.Tasks.ValueTask<({string.Join(", ", nonStreamedReturns.Select(r => $"{r.DataType.FieldTypeString(r.DataTypeIsOptional, currentNamespace)} {r.Name}"))})>",
        };
    }

    /// <summary>Returns the ValueTask return type for a proxy response decode method.</summary>
    internal static string GetProxyResponseReturnType(Operation op, string currentNamespace)
    {
        ImmutableList<Field> nonStreamedReturns = GetNonStreamedReturns(op);
        return nonStreamedReturns.Count switch
        {
            0 => "global::System.Threading.Tasks.ValueTask",
            1 => $"global::System.Threading.Tasks.ValueTask<{nonStreamedReturns[0].DataType.IncomingParameterTypeString(nonStreamedReturns[0].DataTypeIsOptional, currentNamespace)}>",
            _ => $"global::System.Threading.Tasks.ValueTask<({string.Join(", ", nonStreamedReturns.Select(r => $"{r.DataType.IncomingParameterTypeString(r.DataTypeIsOptional, currentNamespace)} {r.Name}"))})>",
        };
    }

    /// <summary>Returns the ValueTask return type for a service request decode method.</summary>
    internal static string GetServiceRequestReturnType(Operation op, string currentNamespace)
    {
        ImmutableList<Field> nonStreamedParams = GetNonStreamedParameters(op);
        return nonStreamedParams.Count switch
        {
            0 => "global::System.Threading.Tasks.ValueTask",
            1 => $"global::System.Threading.Tasks.ValueTask<{nonStreamedParams[0].DataType.IncomingParameterTypeString(nonStreamedParams[0].DataTypeIsOptional, currentNamespace)}>",
            _ => $"global::System.Threading.Tasks.ValueTask<({string.Join(", ", nonStreamedParams.Select(p => $"{p.DataType.IncomingParameterTypeString(p.DataTypeIsOptional, currentNamespace)} {p.Name}"))})>",
        };
    }

    /// <summary>Generates the encode body for operation parameters (used in proxy Request.Encode and service
    /// Response.Encode). Returns null for operations with no non-streamed fields to encode.</summary>
    internal static CodeBlock? GenerateEncodeBody(ImmutableList<Field> fields, string currentNamespace)
    {
        ImmutableList<Field> sortedFields = fields.GetSortedFields().ToImmutableList();
        if (sortedFields.Count == 0)
        {
            return null;
        }

        var body = new CodeBlock();

        // Bit sequence for non-tagged optional fields
        int bitSequenceSize = sortedFields.GetBitSequenceSize();
        if (bitSequenceSize > 0)
        {
            body.WriteLine($"var bitSequenceWriter = encoder.GetBitSequenceWriter({bitSequenceSize});");
        }

        foreach (Field field in sortedFields)
        {
            string param = field.ParameterName;

            if (field.IsTagged)
            {
                body.WriteLine(field.EncodeTaggedField(currentNamespace, paramPrefix: ""));
            }
            else if (field.DataTypeIsOptional)
            {
                string valueParam = field.DataType.IsValueType ? $"{param}.Value" : param;
                CodeBlock encodeExpr = field.DataType.EncodeExpression(currentNamespace, valueParam);
                body.WriteLine($$"""
                    bitSequenceWriter.Write({{param}} != null);
                    if ({{param}} != null)
                    {
                        {{encodeExpr.Indent()}};
                    }
                    """);
            }
            else
            {
                CodeBlock encodeExpr = field.DataType.EncodeExpression(currentNamespace, param);
                body.WriteLine($"{encodeExpr};");
            }
        }

        body.WriteLine("encoder.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
        return body;
    }

    /// <summary>Generates a decode lambda expression for decoding operation fields (parameters or return values).
    /// For a single field, returns a simple lambda. For multiple fields, returns a lambda with a block body
    /// that decodes each field and returns a tuple.</summary>
    internal static string GenerateDecodeLambda(ImmutableList<Field> fields, string currentNamespace)
    {
        if (fields.Count == 1)
        {
            Field field = fields[0];
            string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
            return $"(ref SliceDecoder decoder) => {decodeExpr}";
        }

        // Multiple fields: decode into local variables and return a tuple.
        var body = new CodeBlock();
        foreach (Field field in fields)
        {
            string decodeExpr = field.DataType.DecodeExpression(currentNamespace);
            body.WriteLine($"var sliceP_{field.ParameterName} = {decodeExpr};");
        }
        body.WriteLine($"return ({string.Join(", ", fields.Select(f => $"sliceP_{f.ParameterName}"))});");

        return $$"""
            (ref SliceDecoder decoder) =>
            {
                {{body.Indent()}}
            }
            """;
    }

    /// <summary>Gets the non-streamed parameters for an operation.</summary>
    internal static ImmutableList<Field> GetNonStreamedParameters(Operation op) =>
        op.HasStreamedParameter
            ? op.Parameters.RemoveAt(op.Parameters.Count - 1)
            : op.Parameters;

    /// <summary>Gets the non-streamed return types for an operation.</summary>
    internal static ImmutableList<Field> GetNonStreamedReturns(Operation op) =>
        op.HasStreamedReturn
            ? op.ReturnType.RemoveAt(op.ReturnType.Count - 1)
            : op.ReturnType;

    /// <summary>Returns true if the operation has no streaming parameters or return values.</summary>
    internal static bool IsNonStreaming(Operation op) =>
        !op.HasStreamedParameter && !op.HasStreamedReturn;
}
