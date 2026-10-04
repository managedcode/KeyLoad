using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Strict native argument framing, attributed typed native payloads and stable caller command identity.</summary>
internal static class McpArgumentDecoder
{
    internal static McpDecodedOperation Read<TRequest>(IDictionary<string, JsonElement>? arguments, GrainReadKind kind,
        int maximumPayloadBytes) =>
        new(kind, null, Guid.Empty,
            kind is GrainReadKind.AstQuery or GrainReadKind.LiveQueryStart or GrainReadKind.LiveQueryRead or GrainReadKind.Traverse
                ? InternalNativePayload.SerializePublicInput(Request<TRequest>(arguments, false), maximumPayloadBytes)
                : InternalNativePayload.Serialize(Request<TRequest>(arguments, false), maximumPayloadBytes));

    internal static McpDecodedOperation NoBody(IDictionary<string, JsonElement>? arguments, GrainReadKind kind,
        int maximumPayloadBytes)
    {
        if (arguments is { Count: > 0 })
        { throw InvalidArguments(); }
        return new McpDecodedOperation(kind, null, Guid.Empty, InternalNativePayload.Serialize(0, maximumPayloadBytes));
    }

    internal static McpDecodedOperation Command<TRequest>(IDictionary<string, JsonElement>? arguments,
        OperationKind kind, Func<TRequest, Guid> commandId, int maximumPayloadBytes)
    {
        var request = Request<TRequest>(arguments, false);
        var stableId = commandId(request);
        RequireIdentity(stableId);
        return new McpDecodedOperation(null, kind, stableId, InternalNativePayload.SerializePublicInput(request, maximumPayloadBytes));
    }

    internal static McpDecodedOperation HeaderCommand<TRequest>(IDictionary<string, JsonElement>? arguments, OperationKind kind,
        int maximumPayloadBytes)
    {
        ValidateKeys(arguments, true);
        var identity = arguments![McpCatalogProtocol.CommandId];
        if (identity.ValueKind != JsonValueKind.String || !identity.TryGetGuid(out var stableId))
        {
            throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.StableCommandRequired);
        }
        RequireIdentity(stableId);
        var request = Request<TRequest>(arguments, true);
        return new McpDecodedOperation(null, kind, stableId, InternalNativePayload.SerializePublicInput(request, maximumPayloadBytes));
    }

    internal static TRequest Request<TRequest>(IDictionary<string, JsonElement>? arguments, bool headerCommand)
    {
        ValidateKeys(arguments, headerCommand);
        var value = arguments![McpCatalogProtocol.Request];
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        { throw InvalidArguments(); }
        try
        {
            var request = value.Deserialize<TRequest>(JsonDefaults.Options);
            return request is null ? throw InvalidArguments() : request;
        }
        catch (JsonException) { throw InvalidArguments(); }
    }

    private static void ValidateKeys(IDictionary<string, JsonElement>? arguments, bool headerCommand)
    {
        var expected = headerCommand ? McpCatalogProtocol.HeaderArgumentCount : McpCatalogProtocol.BodyArgumentCount;
        if (arguments is null || arguments.Count != expected)
        { throw InvalidArguments(); }
        foreach (var key in arguments.Keys)
        {
            if (!StringComparer.Ordinal.Equals(key, McpCatalogProtocol.Request) &&
                !(headerCommand && StringComparer.Ordinal.Equals(key, McpCatalogProtocol.CommandId)))
            {
                throw InvalidArguments();
            }
        }
    }

    private static void RequireIdentity(Guid commandId)
    {
        if (commandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.StableCommandRequired); }
    }

    private static KeyLoadException InvalidArguments() => Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.InvalidArguments);
}
