using System.Text.Json;
using KeyLoad.Server;
using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-004/005: metadata replies are bounded and native invoke results remain identical.</summary>
internal sealed class McpGatewayReplyTests
{
    private const string Marker = "native-call-result";
    private const string ToolName = "keyload_documents_get";
    private const string RequestIdKey = "requestId";
    private const string ResultKey = "result";

    [Test]
    public async Task MetadataSuccessHasNullExecutionIdentityAndOwnerLifetime()
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new { tools = Array.Empty<object>() });
        using var expectedDocument = JsonDocument.Parse(canonical);
        var expected = expectedDocument.RootElement.Clone();
        using var owner = McpReplyOwner.Success(canonical, null, canonical.Length + McpFramingProtocol.EnvelopeAllowanceBytes);
        var result = owner.ToolResult();
        var wrapper = result.StructuredContent!.Value;
        await Assert.That(wrapper.GetProperty(RequestIdKey).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(JsonElement.DeepEquals(wrapper.GetProperty(ResultKey), expected)).IsTrue();
        await Assert.That(result.IsError).IsFalse();
    }

    [Test]
    public async Task NativeFunctionResultIdentityAndErrorBitAreNotNormalized()
    {
        using var document = JsonDocument.Parse("{\"secret\":\"" + Marker + "\"}");
        var expected = new CallToolResult { IsError = true, StructuredContent = document.RootElement.Clone() };
        var gatewayResult = new McpGatewayInvokeResult(true, ToolName, "local", ToolName, expected);
        var actual = McpGatewayNativeResult.GetOriginal(gatewayResult);
        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(actual!.IsError).IsTrue();
        await Assert.That(McpGatewayNativeResult.GetOriginal(gatewayResult with { IsSuccess = false })).IsNull();
    }

    [Test]
    public async Task NativeMetadataResultWriterStopsAtInclusiveByteLimit()
    {
        var projection = new McpGatewaySearchProjection([]);
        var exact = McpGatewayMetaResultWriter.Serialize(projection, 256);
        await Assert.That(exact.Length).IsLessThanOrEqualTo(256);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaResultWriter.Serialize(
            new McpGatewaySearchProjection([new McpGatewayScoredTool(new Tool { Name = Marker }, 0.5)]), 1));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(failure.Message).DoesNotContain(Marker);
    }

    [Test]
    public async Task SearchProjectionReturnsCompleteCanonicalToolAndFiniteScore()
    {
        var source = SearchMatch(ToolName, 0.75);
        var projection = McpGatewayMetaProjector.Search(new McpGatewaySearchResult([source], [], "native"), 1);
        var projected = projection.Matches.Single();
        var canonical = McpOperationCatalog.TryGetTool(ToolName, out var descriptor) ? descriptor!.CreateTool() : null;
        await Assert.That(projected.Tool.Name).IsEqualTo(canonical!.Name);
        await Assert.That(JsonElement.DeepEquals(projected.Tool.InputSchema, canonical.InputSchema)).IsTrue();
        await Assert.That(JsonElement.DeepEquals(projected.Tool.OutputSchema!.Value, canonical.OutputSchema!.Value)).IsTrue();
        await Assert.That(projected.Tool.Annotations!.ReadOnlyHint).IsEqualTo(canonical.Annotations!.ReadOnlyHint);
        await Assert.That(projected.Score).IsEqualTo(0.75);
    }

    [Test]
    public async Task RouteProjectionRejectsNonfiniteNativeScores()
    {
        var source = SearchMatch(ToolName, double.NaN);
        var native = new McpGatewayToolRouteResult([new("documents", 1, [source])], [], [], "native");
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => McpGatewayMetaProjector.Route(native, 1, 1));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
    }

    [Test]
    public async Task RouteProjectionReturnsOnlyUniqueCanonicalTools()
    {
        const string secondName = "keyload_documents_commit";
        var first = SearchMatch(ToolName, 0.8);
        var second = SearchMatch(secondName, 0.7);
        var native = new McpGatewayToolRouteResult(
            [new("documents", 0.9, [first, second]), new("duplicate", 0.6, [first])], [], [], "native");
        var projection = McpGatewayMetaProjector.Route(native, 2, 2);
        await Assert.That(projection.Categories.Length).IsEqualTo(1);
        await Assert.That(projection.Categories[0].Tools.Select(item => item.Tool.Name)
            .SequenceEqual([ToolName, secondName])).IsTrue();
        await Assert.That(projection.Categories[0].Score).IsEqualTo(0.9);
    }

    private static McpGatewaySearchMatch SearchMatch(string name, double score)
        => new(name, "local", McpGatewaySourceKind.Local, name, name, "description", [], null, score);
}
