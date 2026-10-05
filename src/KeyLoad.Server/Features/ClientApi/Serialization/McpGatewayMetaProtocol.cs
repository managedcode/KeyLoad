using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns the fixed public gateway-tool names and schemas.</summary>
internal static class McpGatewayMetaProtocol
{
    internal const string SearchName = "gateway_tools_search";
    internal const string RouteName = "gateway_tools_route";
    internal const string InvokeName = "gateway_tool_invoke";
    internal const string Query = "query";
    internal const string MaxResults = "maxResults";
    internal const string MaxCategories = "maxCategories";
    internal const string MaxToolsPerCategory = "maxToolsPerCategory";
    internal const string PreferReadOnly = "preferReadOnly";
    internal const string ToolId = "toolId";
    internal const string Arguments = "arguments";
    internal const int MaximumQueryBytes = 2048;
    internal const int DefaultSearchLimit = 3;
    internal const int MaximumSearchLimit = 4;
    internal const int DefaultRouteLimit = 2;
    internal const int MaximumRouteLimit = 2;
    internal const string InvalidArguments = "The gateway tool arguments do not match the canonical contract.";
    internal const string InvalidCursor = "The gateway tool list does not accept a continuation cursor.";

    private const string SearchSchema = """{"type":"object","properties":{"query":{"type":"string","minLength":1,"maxLength":2048},"maxResults":{"type":"integer","minimum":1,"maximum":4}},"required":["query"],"additionalProperties":false}""";
    private const string RouteSchema = """{"type":"object","properties":{"query":{"type":"string","minLength":1,"maxLength":2048},"maxCategories":{"type":"integer","minimum":1,"maximum":2},"maxToolsPerCategory":{"type":"integer","minimum":1,"maximum":2},"preferReadOnly":{"type":"boolean"}},"required":["query"],"additionalProperties":false}""";
    private const string InvokeSchema = """{"type":"object","properties":{"toolId":{"type":"string","minLength":1},"arguments":{"type":"object"}},"required":["toolId"],"additionalProperties":false}""";
    private const string CanonicalToolSchema = """{"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"},"inputSchema":{"type":"object"},"outputSchema":{"type":"object"},"annotations":{"type":"object","properties":{"readOnlyHint":{"type":"boolean"},"idempotentHint":{"type":"boolean"},"destructiveHint":{"type":"boolean"},"openWorldHint":{"type":"boolean"}},"required":["readOnlyHint","idempotentHint","destructiveHint","openWorldHint"]}},"required":["name","description","inputSchema","outputSchema","annotations"]}""";
    private const string ScoredToolSchema = """{"type":"object","properties":{"tool":CANONICAL_TOOL,"score":{"type":"number"}},"required":["tool","score"],"additionalProperties":false}""";
    private const string FailureEnvelopeSchema = """{"type":"object","properties":{"error":{"type":"object"},"requestId":{"type":["string","null"]}},"required":["error","requestId"],"additionalProperties":false}""";
    private const string SearchOutputSchemaJson = """{"oneOf":[{"type":"object","properties":{"result":{"type":"object","properties":{"matches":{"type":"array","items":SCORED_TOOL}},"required":["matches"],"additionalProperties":false},"requestId":{"type":["string","null"]}},"required":["result","requestId"],"additionalProperties":false},FAILURE]}""";
    private const string RouteOutputSchemaJson = """{"oneOf":[{"type":"object","properties":{"result":{"type":"object","properties":{"categories":{"type":"array","items":{"type":"object","properties":{"category":{"type":"string"},"score":{"type":"number"},"tools":{"type":"array","items":SCORED_TOOL}},"required":["category","score","tools"],"additionalProperties":false}}},"required":["categories"],"additionalProperties":false},"requestId":{"type":["string","null"]}},"required":["result","requestId"],"additionalProperties":false},FAILURE]}""";
    private const string InvokeOutputSchemaJson = """{"oneOf":[{"type":"object","properties":{"result":{},"requestId":{"type":["string","null"]}},"required":["result","requestId"],"additionalProperties":false},FAILURE]}""";
    private static readonly JsonElement SearchInputSchema = ParseSchema(SearchSchema);
    private static readonly JsonElement RouteInputSchema = ParseSchema(RouteSchema);
    private static readonly JsonElement InvokeInputSchema = ParseSchema(InvokeSchema);
    private static readonly string ScoredToolSchemaJson = ScoredToolSchema
        .Replace("CANONICAL_TOOL", CanonicalToolSchema, StringComparison.Ordinal);
    private static readonly JsonElement SearchOutputSchema = ParseSchema(SearchOutputSchemaJson
        .Replace("SCORED_TOOL", ScoredToolSchemaJson, StringComparison.Ordinal)
        .Replace("FAILURE", FailureEnvelopeSchema, StringComparison.Ordinal));
    private static readonly JsonElement RouteOutputSchema = ParseSchema(RouteOutputSchemaJson
        .Replace("SCORED_TOOL", ScoredToolSchemaJson, StringComparison.Ordinal)
        .Replace("FAILURE", FailureEnvelopeSchema, StringComparison.Ordinal));
    private static readonly JsonElement InvokeOutputSchema = ParseSchema(InvokeOutputSchemaJson
        .Replace("FAILURE", FailureEnvelopeSchema, StringComparison.Ordinal));

    internal static IReadOnlyList<Tool> CreateTools() =>
    [
        CreateTool(SearchName, "Find relevant KeyLoad operations by task description.", SearchInputSchema, SearchOutputSchema),
        CreateTool(RouteName, "Route a task to a small set of relevant KeyLoad operations.", RouteInputSchema, RouteOutputSchema),
        CreateTool(InvokeName, "Invoke one exact canonical KeyLoad operation.", InvokeInputSchema, InvokeOutputSchema)
    ];

    internal static bool TryGetOperation(string name, out McpGatewayMetaOperation operation)
    {
        operation = name switch
        {
            SearchName => McpGatewayMetaOperation.Search,
            RouteName => McpGatewayMetaOperation.Route,
            InvokeName => McpGatewayMetaOperation.Invoke,
            _ => McpGatewayMetaOperation.None
        };
        return operation != McpGatewayMetaOperation.None;
    }

    internal static string NameFor(McpGatewayMetaOperation operation) => operation switch
    {
        McpGatewayMetaOperation.Search => SearchName,
        McpGatewayMetaOperation.Route => RouteName,
        McpGatewayMetaOperation.Invoke => InvokeName,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static Tool CreateTool(string name, string description, JsonElement input, JsonElement output)
        => new()
        {
            Name = name,
            Description = description,
            InputSchema = input.Clone(),
            OutputSchema = output.Clone(),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = name != InvokeName,
                IdempotentHint = name != InvokeName,
                DestructiveHint = name == InvokeName,
                OpenWorldHint = false
            }
        };

    private static JsonElement ParseSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }
}
