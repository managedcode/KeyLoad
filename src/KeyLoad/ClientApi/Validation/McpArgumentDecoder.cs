using System.Text.Json;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Strict execution argument framing, typed operation inputs and stable caller command identity.</summary>
internal static class McpArgumentDecoder
{
    internal static McpDecodedOperation Read<TRequest>(IDictionary<string, JsonElement>? arguments, GrainReadKind kind)
        where TRequest : notnull
        => (new(kind, null, Guid.Empty, (Request<TRequest>(arguments, false) /* KLD0042: Erase the validated TRequest only at the decoded descriptor slot; the existing public administrative operation accepts bool as well as class DTOs. */)) /* KLD0041: Create the existing decoded descriptor with its closed kind and validated input, including the public bool administrative input or native no-body marker. */);

    internal static McpDecodedOperation NoBody(IDictionary<string, JsonElement>? arguments, GrainReadKind kind)
    {
        const int ArgumentsEmptyCount = 0;

        if (arguments is { Count: > ArgumentsEmptyCount })
        {
            throw InvalidArguments();
        }
        return (new McpDecodedOperation(kind, null, Guid.Empty, (GrainContracts.NoDtoMarker /* KLD0042: The current no-body envelope marker is the existing integer sentinel; erase it only into the decoded descriptor's native payload slot. */)) /* KLD0041: Create the existing decoded descriptor with its closed kind and validated input, including the public bool administrative input or native no-body marker. */);
    }

    internal static McpDecodedOperation Command<TRequest>(IDictionary<string, JsonElement>? arguments,
        OperationKind kind, Func<TRequest, Guid> commandId) where TRequest : notnull
    {
        var request = Request<TRequest>(arguments, false);
        var stableId = commandId(request);
        RequireIdentity(stableId);
        return (new McpDecodedOperation(null, kind, stableId, (request /* KLD0042: Erase the validated TRequest only at the decoded descriptor slot; the existing public administrative operation accepts bool as well as class DTOs. */)) /* KLD0041: Create the existing decoded descriptor with its closed kind and validated input, including the public bool administrative input or native no-body marker. */);
    }

    internal static McpDecodedOperation HeaderCommand<TRequest>(IDictionary<string, JsonElement>? arguments, OperationKind kind)
        where TRequest : notnull
    {
        ValidateKeys(arguments, true);
        var identity = arguments![McpCatalogProtocol.CommandId];
        if (identity.ValueKind != JsonValueKind.String || !identity.TryGetGuid(out var stableId))
        {
            throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.StableCommandRequired);
        }
        RequireIdentity(stableId);
        var request = Request<TRequest>(arguments, true);
        return (new McpDecodedOperation(null, kind, stableId, (request /* KLD0042: Erase the validated TRequest only at the decoded descriptor slot; the existing public administrative operation accepts bool as well as class DTOs. */)) /* KLD0041: Create the existing decoded descriptor with its closed kind and validated input, including the public bool administrative input or native no-body marker. */);
    }

    internal static TRequest Request<TRequest>(IDictionary<string, JsonElement>? arguments, bool headerCommand) where TRequest : notnull
    {
        ValidateKeys(arguments, headerCommand);
        var value = arguments![McpCatalogProtocol.Request];
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw InvalidArguments();
        }
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
        {
            throw InvalidArguments();
        }
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
        {
            throw Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.StableCommandRequired);
        }
    }

    private static KeyLoadException InvalidArguments() => Errors.Fail(ErrorCode.Validation, McpCatalogProtocol.InvalidArguments);
}
