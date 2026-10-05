using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingMcpContractTests
{
    [Test]
    public async Task NativeAliasesAndFieldIdsRemainExact()
        => await GraphIncomingMcpNativeAssertions.VerifyAsync();

    [Test]
    public async Task NativeAndPublicJsonRoundTripsPreserveFullIncomingEdgeAndProjection()
    {
        var request = GraphIncomingMcpTestData.Request();
        var requestBytes = NativeSerialization.Serialize(request);
        var nativeRequest = NativeSerialization.Deserialize<ReadIncomingGraphEdgesRequestV1>(requestBytes)!;
        await Assert.That(nativeRequest.Version).IsEqualTo(1);
        await Assert.That(nativeRequest.Target).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(nativeRequest.Graph).IsEqualTo(GraphIncomingMcpTestData.Graph);
        await Assert.That(nativeRequest.Limit).IsEqualTo(GraphIncomingMcpTestData.Limit);
        var requestJson = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options);
        var publicRequest = JsonSerializer.Deserialize<ReadIncomingGraphEdgesRequestV1>(requestJson, JsonDefaults.Options)!;
        await Assert.That(NativeSerialization.Serialize(nativeRequest).AsSpan().SequenceEqual(requestBytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(request)),
            JsonNode.Parse(JsonDefaults.Serialize(publicRequest)))).IsTrue();
        await VerifyPageRoundTripAsync();
    }

    [Test]
    public async Task LocalReadCatalogMatchesIncomingToolContractAndEffectHints()
        => await GraphIncomingMcpCatalogAssertions.VerifyAsync();

    private static async Task VerifyPageRoundTripAsync()
    {
        var page = GraphIncomingMcpTestData.Page();
        var bytes = NativeSerialization.Serialize(page);
        var native = NativeSerialization.Deserialize<GraphIncomingEdgesPageV1>(bytes)!;
        var publicPage = JsonSerializer.Deserialize<GraphIncomingEdgesPageV1>(
            JsonSerializer.SerializeToUtf8Bytes(page, JsonDefaults.Options), JsonDefaults.Options)!;
        await Assert.That(native.Version).IsEqualTo(1);
        await Assert.That(native.CutPosition).IsEqualTo(GraphIncomingMcpTestData.CutPosition);
        await Assert.That(native.Projection).IsEqualTo(GraphIncomingMcpTestData.Projection);
        await Assert.That(native.Rows.Length).IsEqualTo(2);
        await Assert.That(native.Rows[0].DeliveredRevision).IsEqualTo(GraphIncomingMcpTestData.LocalDeliveredRevision);
        await Assert.That(native.Rows[0].Edge.Id).IsEqualTo(GraphIncomingMcpTestData.EdgeId);
        await Assert.That(native.Rows[0].Edge.From).IsEqualTo(GraphIncomingMcpTestData.Source);
        await Assert.That(native.Rows[0].Edge.To).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(native.Rows[0].Edge.Label).IsEqualTo(GraphIncomingMcpTestData.Label);
        await Assert.That(native.Rows[0].Edge.AttributesJson).IsEqualTo(GraphIncomingMcpTestData.AttributesJson);
        await Assert.That(native.Rows[0].Edge.Revision).IsEqualTo(GraphIncomingMcpTestData.SourceRevision);
        await Assert.That(native.Rows[1].DeliveredRevision).IsEqualTo(GraphIncomingMcpTestData.DeliveredRevision);
        await Assert.That(native.Rows[1].Edge.Id).IsEqualTo(GraphIncomingMcpTestData.CrossEdgeId);
        await Assert.That(native.Rows[1].Edge.From).IsEqualTo(GraphIncomingMcpTestData.CrossSource);
        await Assert.That(native.Rows[1].Edge.To).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(native.Rows[1].Edge.Revision).IsEqualTo(GraphIncomingMcpTestData.CrossSourceRevision);
        await Assert.That(NativeSerialization.Serialize(native).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(page)),
            JsonNode.Parse(JsonDefaults.Serialize(publicPage)))).IsTrue();
    }
}
