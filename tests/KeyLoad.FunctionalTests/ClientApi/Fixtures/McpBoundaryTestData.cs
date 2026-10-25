using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Owns actual public SDK encoding, metadata and bounded reply objects for execution boundary checks.</summary>
internal static class McpBoundaryTestData
{
    internal const string Marker = "private-execution-boundary-marker";
    internal const string UnicodeName = "café-private-execution-boundary-marker";
    internal const string UnicodeCharacter = "é";
    internal const string InvalidBase64 = "=?base64?private-execution-boundary-marker?=";
    internal const string InvalidUtf8 = "=?base64?/w==?=";
    internal const string EmptyEncoded = "=?base64??=";
    internal const string IncompleteEncoded = "=?base64?private-execution-boundary-marker";
    internal const string InvalidTransport = "The MCP transport headers or metadata are invalid.";
    internal const string MetaField = "_meta";
    internal const string OldPayloadField = "oldPayload";
    internal const string OldPayload = "discarded-private-execution-payload";
    internal const string ContentField = "content";
    internal const string StructuredContentField = "structuredContent";
    internal const string IsErrorField = "isError";
    internal const string MissingNode = "Expected an actual execution serialized node.";
    internal const char Padding = 'a';
    internal const char LineFeed = '\n';
    internal const char Delete = '\u007F';
    internal const int AcceptedPaddingCharacters = 181;
    internal const int AcceptedEncodedCharacters = 255;
    internal const int RejectedEncodedCharacters = 259;
    private const string MetadataName = "KeyLoad café 😀";
    private const string MetadataVersion = "1.0.0";
    private const string MetadataExtraField = "trace";
    private const int MetadataValue = 17;

    /// <summary>Uses the genuine execution encoder without nullable-forgiving assertions.</summary>
    /// <param name="name">The concrete target passed to the official SDK encoder.</param>
    /// <returns>The actual encoded field.</returns>
    internal static string Encode(string name) => McpHeaderEncoder.EncodeValue(name)
        ?? throw new InvalidOperationException(MissingNode);

    /// <summary>Creates injected execution server identity and additional bounded metadata values.</summary>
    /// <returns>The exact metadata node transferred to the fresh response without cloning.</returns>
    internal static JsonObject Metadata() => new()
    {
        [MetaKeys.ServerInfo] = JsonSerializer.SerializeToNode(
            new Implementation { Name = MetadataName, Version = MetadataVersion }, McpJsonUtilities.DefaultOptions),
        [MetadataExtraField] = new JsonArray(Marker, MetadataValue, true)
    };

    /// <summary>Creates a genuine owned tool result using the actual safe error or canonical success factory.</summary>
    /// <param name="failure">Whether the result is a safe domain error.</param>
    /// <returns>The reply owner retained throughout execution replacement and serialization.</returns>
    internal static McpReplyOwner Reply(bool failure) => failure
        ? McpReplyOwner.Failure(ErrorCode.PermissionDenied, McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes)
        : McpReplyOwner.Success(JsonDefaults.Serialize(UnicodeName), McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
}
