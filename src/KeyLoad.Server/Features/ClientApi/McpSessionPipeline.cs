using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Uses native message filter seams to admit before typed conversion and sanitize before SSE.</summary>
internal sealed class McpSessionPipeline(HttpContext context, McpRequestState state)
{
    private const string ProtocolFailure = "The MCP request could not be completed.";

    /// <summary>Wraps actual native incoming processing with one canonical operation admission.</summary>
    internal McpMessageHandler Incoming(McpMessageHandler next) => async (message, cancellationToken) =>
    {
        var request = message.JsonRpcMessage as JsonRpcRequest;
        var tool = request?.Method == RequestMethods.ToolsCall;
        try
        {
            state.Admit(tool ? Select(request!) : null, cancellationToken);
        }
        catch (KeyLoadException error)
        {
            if (request is not null)
            { await RejectAsync(message, request, tool, error.Code, cancellationToken).ConfigureAwait(false); }
            return;
        }
        await next(message, cancellationToken).ConfigureAwait(false);
    };

    /// <summary>Bounds the complete native message after server metadata and replaces unsafe error details.</summary>
    internal McpMessageHandler Outgoing(McpMessageHandler next) => async (message, cancellationToken) =>
    {
        if (message.JsonRpcMessage is JsonRpcError nativeError)
        { nativeError.Error = SafeProtocolError(nativeError.Error.Code); }
        var maximumBytes = checked(state.MaximumReplyBytes + McpFramingProtocol.EnvelopeAllowanceBytes);
        try
        { McpNativeOutput.Validate(message.JsonRpcMessage, maximumBytes); }
        catch (KeyLoadException error)
        {
            if (message.JsonRpcMessage is JsonRpcResponse response)
            {
                McpResponseReplacement.Apply(response, state.Failure(error.Code, CanonicalOperationGateway.RequestId(context)));
            }
            else if (message.JsonRpcMessage is JsonRpcError failed)
            { failed.Error = SafeProtocolError((int)McpErrorCode.InternalError); }
            else
            { throw; }
            McpNativeOutput.Validate(message.JsonRpcMessage, McpFramingProtocol.EnvelopeAllowanceBytes);
        }
        await next(message, cancellationToken).ConfigureAwait(false);
    };

    private static McpOperationDescriptor Select(JsonRpcRequest request)
    {
        if (request.Params is not JsonObject parameters || parameters[McpTransportProtocol.Name] is not JsonValue name
            || !name.TryGetValue<string>(out var toolName) || string.IsNullOrWhiteSpace(toolName))
        { throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.InvalidArguments); }
        return McpOperationCatalog.TryGet(toolName, out var descriptor) ? descriptor
            : throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation);
    }

    private async Task RejectAsync(MessageContext message, JsonRpcRequest request, bool tool, ErrorCode code,
        CancellationToken cancellationToken)
    {
        JsonRpcMessage reply = tool ? new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(state.Failure(code, null), McpJsonUtilities.DefaultOptions)
        } : new JsonRpcError { Id = request.Id, Error = SafeProtocolError((int)McpErrorCode.InternalError) };
        await message.Server.SendMessageAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    private static JsonRpcErrorDetail SafeProtocolError(int code) => new() { Code = code, Message = ProtocolFailure };
}
