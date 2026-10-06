using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Validates bounded public meta arguments without accepting gateway context.</summary>
internal static class McpGatewayMetaArgumentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> SearchKeys = new(StringComparer.Ordinal)
        { McpGatewayMetaProtocol.Query, McpGatewayMetaProtocol.MaxResults };
    private static readonly HashSet<string> RouteKeys = new(StringComparer.Ordinal)
        { McpGatewayMetaProtocol.Query, McpGatewayMetaProtocol.MaxCategories,
            McpGatewayMetaProtocol.MaxToolsPerCategory, McpGatewayMetaProtocol.PreferReadOnly };
    private static readonly HashSet<string> InvokeKeys = new(StringComparer.Ordinal)
        { McpGatewayMetaProtocol.ToolId, McpGatewayMetaProtocol.Arguments };

    internal static McpGatewayMetaSelection Select(JsonRpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Params is not JsonObject parameters
            || parameters[McpTransportProtocol.Name] is not JsonValue nameNode
            || !nameNode.TryGetValue<string>(out var name))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation); }
        return Select(name, parameters[McpGatewayMetaProtocol.Arguments]);
    }

    internal static McpGatewayMetaSelection Select(string? name, JsonNode? arguments)
    {
        if (string.IsNullOrWhiteSpace(name) || !McpGatewayMetaProtocol.TryGetOperation(name, out var operation))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation); }
        if (operation != McpGatewayMetaOperation.Invoke)
        { return new(operation, null); }
        if (arguments is not JsonObject invokeArguments
            || invokeArguments[McpGatewayMetaProtocol.ToolId] is not JsonValue toolNode
            || !toolNode.TryGetValue<string>(out var toolId) || string.IsNullOrWhiteSpace(toolId))
        { throw InvalidArguments(); }
        if (!McpOperationCatalog.TryGetTool(toolId, out var descriptor))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation); }
        return new(operation, descriptor);
    }

    internal static McpGatewayMetaRequest Read(McpGatewayMetaOperation operation,
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || !HasExactKeys(operation, arguments))
        { throw InvalidArguments(); }
        return operation switch
        {
            McpGatewayMetaOperation.Search => ReadSearch(arguments),
            McpGatewayMetaOperation.Route => ReadRoute(arguments),
            McpGatewayMetaOperation.Invoke => ReadInvoke(arguments),
            _ => throw InvalidArguments()
        };
    }

    private static bool HasExactKeys(McpGatewayMetaOperation operation, IDictionary<string, JsonElement> arguments)
    {
        var allowed = operation switch
        {
            McpGatewayMetaOperation.Search => SearchKeys,
            McpGatewayMetaOperation.Route => RouteKeys,
            McpGatewayMetaOperation.Invoke => InvokeKeys,
            _ => null
        };
        return allowed is not null && arguments.Keys.All(allowed.Contains);
    }

    private static McpGatewayMetaRequest ReadSearch(IDictionary<string, JsonElement> arguments)
    {
        var query = ReadQuery(arguments);
        var limit = ReadOptionalInt(arguments, McpGatewayMetaProtocol.MaxResults,
            McpGatewayMetaProtocol.DefaultSearchLimit, McpGatewayMetaProtocol.MaximumSearchLimit);
        return new(McpGatewayMetaOperation.Search, query, limit);
    }

    private static McpGatewayMetaRequest ReadRoute(IDictionary<string, JsonElement> arguments)
    {
        var query = ReadQuery(arguments);
        var categories = ReadOptionalInt(arguments, McpGatewayMetaProtocol.MaxCategories,
            McpGatewayMetaProtocol.DefaultRouteLimit, McpGatewayMetaProtocol.MaximumRouteLimit);
        var tools = ReadOptionalInt(arguments, McpGatewayMetaProtocol.MaxToolsPerCategory,
            McpGatewayMetaProtocol.DefaultRouteLimit, McpGatewayMetaProtocol.MaximumRouteLimit);
        bool? prefer = arguments.TryGetValue(McpGatewayMetaProtocol.PreferReadOnly, out var value)
            ? ReadBoolean(value) : null;
        return new(McpGatewayMetaOperation.Route, query, CategoryLimit: categories,
            ToolsPerCategory: tools, PreferReadOnly: prefer);
    }

    private static McpGatewayMetaRequest ReadInvoke(IDictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue(McpGatewayMetaProtocol.ToolId, out var toolValue)
            || toolValue.ValueKind != JsonValueKind.String)
        { throw InvalidArguments(); }
        var toolId = toolValue.GetString();
        if (string.IsNullOrWhiteSpace(toolId))
        { throw InvalidArguments(); }
        if (!McpOperationCatalog.TryGetTool(toolId, out _))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, McpCatalogProtocol.InvalidOperation); }
        var values = arguments.TryGetValue(McpGatewayMetaProtocol.Arguments, out var nested)
            ? ReadObject(nested) : new Dictionary<string, object?>(StringComparer.Ordinal);
        return new(McpGatewayMetaOperation.Invoke, ToolId: toolId, Arguments: values);
    }

    private static string ReadQuery(IDictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue(McpGatewayMetaProtocol.Query, out var value)
            || value.ValueKind != JsonValueKind.String)
        { throw InvalidArguments(); }
        var query = value.GetString();
        if (string.IsNullOrWhiteSpace(query))
        { throw InvalidArguments(); }
        try
        {
            if (StrictUtf8.GetByteCount(query) > McpGatewayMetaProtocol.MaximumQueryBytes)
            { throw InvalidArguments(); }
        }
        catch (EncoderFallbackException)
        { throw InvalidArguments(); }
        return query;
    }

    private static int ReadOptionalInt(IDictionary<string, JsonElement> arguments, string key, int fallback, int maximum)
    {
        const int ParsedValidationBoundary = 1;

        if (!arguments.TryGetValue(key, out var value))
        { return fallback; }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed) || parsed < ParsedValidationBoundary || parsed > maximum)
        { throw InvalidArguments(); }
        return parsed;
    }

    private static bool ReadBoolean(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        { return value.GetBoolean(); }
        throw InvalidArguments();
    }

    private static Dictionary<string, object?> ReadObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        { throw InvalidArguments(); }
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value.Clone()))
            { throw InvalidArguments(); }
        }
        return result;
    }

    private static KeyLoadException InvalidArguments()
        => Errors.Fail(ErrorCode.Validation, McpGatewayMetaProtocol.InvalidArguments);
}
