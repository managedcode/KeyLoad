using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class SnapshotInstallFeedCutAssertions
{
    private const string Collection = "snapshots";
    private const string Document = "ordered-tail";
    private const string Json = "{\"tail\":true}";
    private const string PointQuery = "SELECT * FROM snapshots WHERE id = 'ordered-tail' ORDER BY id";
    private const string PointPath = "point";
    private const long Revision = 1;

    internal static async Task<long> ObserveAsync(KeyLoadClient sdk, PartitionRef partition,
        CommitReceipt tail, NodeStatus installed, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await RequireOwnerAsync(before, installed, tail);
        var reference = new EntityRef(partition, Collection, Document);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, tail.Token, token));
        await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, Revision, Json, false, []), document);
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(new(partition, PointQuery), token));
        await SqlRf3Protocol.EqualAsync(new QueryPage(
            [new(Document, Revision, Json, false, [])], null, page.CutPosition, PointPath), page);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token));
        await RequireOwnerAsync(after, installed, tail);
        await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        return page.CutPosition;
    }

    private static async Task RequireOwnerAsync(NodeStatus actual, NodeStatus installed, CommitReceipt tail)
    {
        await Assert.That(actual.NodeId).IsEqualTo(installed.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(installed.Incarnation);
        await Assert.That(actual.Incarnation).IsEqualTo(tail.Token.Incarnation);
        await Assert.That(actual.RoutingReady).IsTrue();
        await Assert.That(actual.Voters).IsEqualTo(installed.Voters);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Applied).IsGreaterThanOrEqualTo(tail.Token.Position);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(installed.ReadGeneration);
    }
}
