using KeyLoad.IntegrationTests.Features.ClientApi;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphIncomingRf3Assertions
{
    private const string Projection = GraphIncomingRf3Scenario.Projection;
    private const string CrossEdgeId = "same-edge";
    private const string LocalEdgeId = "local-edge";
    private const string MissingRow = "The expected graph incoming row was absent.";

    internal static async Task AssertThreeRowsAsync(GraphIncomingEdgesPageV1 page,
        GraphIncomingRf3Seed seed)
    {
        await Assert.That(page.Version).IsEqualTo(1);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.Projection).IsEqualTo(Projection);
        await Assert.That(page.Rows.Length).IsEqualTo(3);
        await Assert.That(page.Rows.Select(row => row.Edge.From).SequenceEqual(
            [seed.FirstSource, seed.SecondSource, seed.LocalSource])).IsTrue();
        var rows = page.Rows.ToArray();
        await AssertRowAsync(rows[0], seed.FirstSource, seed.Target, CrossEdgeId, 1, 1,
            GraphIncomingRf3Scenario.PublicMarker + "-first");
        await AssertRowAsync(rows[1], seed.SecondSource, seed.Target, CrossEdgeId, 1, 1,
            GraphIncomingRf3Scenario.PublicMarker + "-second");
        await AssertRowAsync(rows[2], seed.LocalSource, seed.Target, LocalEdgeId, 1, 0,
            GraphIncomingRf3Scenario.PublicMarker + "-local");
    }

    internal static async Task AssertEquivalentAsync(GraphIncomingEdgesPageV1 expected,
        GraphIncomingEdgesPageV1 actual)
    {
        await Assert.That(actual.Version).IsEqualTo(expected.Version);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await Assert.That(actual.Projection).IsEqualTo(expected.Projection);
        await Assert.That(actual.Rows.Length).IsEqualTo(expected.Rows.Length);
        for (var index = 0; index < expected.Rows.Length; index++)
        {
            await AssertEdgeAsync(actual.Rows[index], expected.Rows[index]);
        }
    }

    internal static async Task AssertSourceHiddenAsync(GraphIncomingEdgesPageV1 page,
        GraphIncomingRf3Seed seed)
    {
        await Assert.That(page.Projection).IsEqualTo(Projection);
        await Assert.That(page.Rows.Length).IsEqualTo(1);
        await AssertRowAsync(page.Rows[0], seed.LocalSource, seed.Target, LocalEdgeId, 1, 0,
            GraphIncomingRf3Scenario.PublicMarker + "-local");
    }

    internal static async Task AssertFailureAsync(Result<GraphIncomingEdgesPageV1> result,
        ErrorCode expected)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    internal static async Task AssertMcpFailureAsync(CallToolResult result, ErrorCode expected)
        => await McpCallerAssertions.ErrorAsync(result, expected, dispatched: true);

    internal static async Task AssertNoPrivatePayloadAsync(CallToolResult result, string credential)
        => await McpCallerAssertions.DoesNotDiscloseAsync(result, credential,
            GraphIncomingRf3Scenario.PrivateMarker);

    internal static async Task AssertExpectedRowAsync(GraphIncomingEdgesPageV1 page, EntityRef source,
        EntityRef target, string edgeId, long revision, long deliveredRevision, string publicMarker)
    {
        var row = page.Rows.SingleOrDefault(candidate => candidate.Edge.From == source
            && candidate.Edge.Id == edgeId);
        await Assert.That(row is not null).IsTrue().Because(MissingRow);
        await AssertRowAsync(row!, source, target, edgeId, revision, deliveredRevision, publicMarker);
    }

    private static async Task AssertRowAsync(GraphIncomingEdgeRowV1 row, EntityRef source, EntityRef target,
        string edgeId, long edgeRevision, long deliveredRevision, string publicMarker)
    {
        await Assert.That(row.Edge.Id).IsEqualTo(edgeId);
        await Assert.That(row.Edge.From).IsEqualTo(source);
        await Assert.That(row.Edge.To).IsEqualTo(target);
        await Assert.That(row.Edge.Label).IsEqualTo(GraphIncomingRf3Scenario.Label);
        await Assert.That(row.Edge.Revision).IsEqualTo(edgeRevision);
        await Assert.That(row.DeliveredRevision).IsEqualTo(deliveredRevision);
        await Assert.That(row.Edge.AttributesJson).IsEqualTo("{\"public\":\"" + publicMarker + "\"}");
    }

    private static async Task AssertEdgeAsync(GraphIncomingEdgeRowV1 actual,
        GraphIncomingEdgeRowV1 expected)
    {
        await Assert.That(actual.Edge.Id).IsEqualTo(expected.Edge.Id);
        await Assert.That(actual.Edge.From).IsEqualTo(expected.Edge.From);
        await Assert.That(actual.Edge.To).IsEqualTo(expected.Edge.To);
        await Assert.That(actual.Edge.Label).IsEqualTo(expected.Edge.Label);
        await Assert.That(actual.Edge.AttributesJson).IsEqualTo(expected.Edge.AttributesJson);
        await Assert.That(actual.Edge.Revision).IsEqualTo(expected.Edge.Revision);
        await Assert.That(actual.DeliveredRevision).IsEqualTo(expected.DeliveredRevision);
    }
}
