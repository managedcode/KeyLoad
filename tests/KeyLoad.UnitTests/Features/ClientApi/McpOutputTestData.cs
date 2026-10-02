using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Owns genuine canonical JSON and native response objects for explicit output-lifetime proofs.</summary>
internal static class McpOutputTestData
{
    internal const int MaximumBytes = 65_536;
    internal const int MaximumSummaryCharacters = 128;
    internal const int OverReplyDepth = 62;
    internal const int NativeResultDepth = 63;
    internal const int LongBusinessIdentifierBytes = 257;
    internal const string ResultField = "result";
    internal const string ErrorField = "error";
    internal const string DetailField = "detail";
    internal const string RequestIdField = "requestId";
    internal const string IdentifierField = "id";
    internal const string NativeIdentifier = "native-output-request";
    internal const string Marker = "private-output-marker";
    internal const string SafeProtocolError = "The MCP request is invalid.";
    internal const string InvalidJson = "{\"private-output-marker\":]}";
    internal const string CanonicalJson = "{\"integer\":9007199254740993,\"scaled\":1.2300e+2,\"text\":\"quote:\\\" café 😀 private-output-marker\",\"escaped\":\"\\u00E9\"}";
    private const string ExecutionIdentifier = "ba90ef80-70e0-453d-9426-96ba3e4d9b93";
    private const string MetaField = "_meta";
    private const string MetadataNameField = "name";
    private const string MetadataVersionField = "version";
    private const string MetadataName = "KeyLoad café 😀 \"server\"";
    private const string MetadataVersion = "1.0.0";
    internal static readonly Guid ExecutionId = Guid.Parse(ExecutionIdentifier);

    /// <summary>Creates a real polymorphic SDK response with already injected server-info metadata.</summary>
    /// <param name="tool">The native result borrowing its reply owner's document.</param>
    /// <returns>The exact response object supplied to the product validator and native serializer.</returns>
    internal static JsonRpcResponse Response(CallToolResult tool)
    {
        var result = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)!;
        result[MetaField] = new JsonObject
        {
            [MetaKeys.ServerInfo] = new JsonObject { [MetadataNameField] = MetadataName, [MetadataVersionField] = MetadataVersion }
        };
        return new JsonRpcResponse { Id = new RequestId(NativeIdentifier), Result = result };
    }
}
