using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingMcpContractTests
{
    [Test]
    public async Task AliasesAndFieldIdsRemainExact()
        => await GraphIncomingMcpAssertions.VerifyAsync();

    [Test]
    public async Task AndPublicJsonRoundTripsPreserveFullIncomingEdgeAndProjection()
    {
        var request = GraphIncomingMcpTestData.Request();
        var requestBytes = OrleansSerialization.Serialize(request);
        var runtimeRequest = OrleansSerialization.Deserialize<ReadIncomingGraphEdgesRequest>(requestBytes)!;
        await Assert.That(runtimeRequest.Version).IsEqualTo(1);
        await Assert.That(runtimeRequest.Target).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(runtimeRequest.Graph).IsEqualTo(GraphIncomingMcpTestData.Graph);
        await Assert.That(runtimeRequest.Limit).IsEqualTo(GraphIncomingMcpTestData.Limit);
        var requestJson = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options);
        var publicRequest = JsonSerializer.Deserialize<ReadIncomingGraphEdgesRequest>(requestJson, JsonDefaults.Options)!;
        await Assert.That(OrleansSerialization.Serialize(runtimeRequest).AsSpan().SequenceEqual(requestBytes)).IsTrue();
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
        var bytes = OrleansSerialization.Serialize(page);
        var execution = OrleansSerialization.Deserialize<GraphIncomingEdgesPage>(bytes)!;
        var publicPage = JsonSerializer.Deserialize<GraphIncomingEdgesPage>(
            JsonSerializer.SerializeToUtf8Bytes(page, JsonDefaults.Options), JsonDefaults.Options)!;
        await Assert.That(execution.Version).IsEqualTo(1);
        await Assert.That(execution.CutPosition).IsEqualTo(GraphIncomingMcpTestData.CutPosition);
        await Assert.That(execution.Projection).IsEqualTo(GraphIncomingMcpTestData.Projection);
        await Assert.That(execution.Rows.Length).IsEqualTo(2);
        await Assert.That(execution.Rows[0].DeliveredRevision).IsEqualTo(GraphIncomingMcpTestData.LocalDeliveredRevision);
        await Assert.That(execution.Rows[0].Edge.Id).IsEqualTo(GraphIncomingMcpTestData.EdgeId);
        await Assert.That(execution.Rows[0].Edge.From).IsEqualTo(GraphIncomingMcpTestData.Source);
        await Assert.That(execution.Rows[0].Edge.To).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(execution.Rows[0].Edge.Label).IsEqualTo(GraphIncomingMcpTestData.Label);
        await Assert.That(execution.Rows[0].Edge.AttributesJson).IsEqualTo(GraphIncomingMcpTestData.AttributesJson);
        await Assert.That(execution.Rows[0].Edge.Revision).IsEqualTo(GraphIncomingMcpTestData.SourceRevision);
        await Assert.That(execution.Rows[1].DeliveredRevision).IsEqualTo(GraphIncomingMcpTestData.DeliveredRevision);
        await Assert.That(execution.Rows[1].Edge.Id).IsEqualTo(GraphIncomingMcpTestData.CrossEdgeId);
        await Assert.That(execution.Rows[1].Edge.From).IsEqualTo(GraphIncomingMcpTestData.CrossSource);
        await Assert.That(execution.Rows[1].Edge.To).IsEqualTo(GraphIncomingMcpTestData.Target);
        await Assert.That(execution.Rows[1].Edge.Revision).IsEqualTo(GraphIncomingMcpTestData.CrossSourceRevision);
        await Assert.That(OrleansSerialization.Serialize(execution).AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(JsonDefaults.Serialize(page)),
            JsonNode.Parse(JsonDefaults.Serialize(publicPage)))).IsTrue();
    }
}
