using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.ChangeFeeds;

internal static class LiveQueryCutClusterAssertions
{
    internal const string QueryTool = "keyload_query_ast";
    private const string FullScanPath = "bounded-full-scan";
    private const int SiloCount = 3;

    internal static async Task<Func<Task<long>>> CreateAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, CommitReceipt receipt,
        ImmutableArray<QueryRow> rows, Func<Task<QueryPage>> query, CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        return async () =>
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
            await OwnerAsync(before, owner, receipt);
            var document = await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.GetAsync(scenario.Reference, receipt.Token, token));
            DocumentResult? expected = rows.IsEmpty ? null : new(scenario.StoredReference,
                rows.Single().Revision, rows.Single().Json, rows.Single().Redacted,
                [FeedLiveClusterProtocol.SecretField]);
            await SqlClusterProtocol.EqualAsync(expected, document.Document);
            var page = await query();
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
            await SqlClusterProtocol.EqualAsync(new QueryPage(rows, null, page.CutPosition, FullScanPath), page);
            var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
            await OwnerAsync(after, owner, receipt);
            await Assert.That(after.Position).IsGreaterThanOrEqualTo(before.Position);
            await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
            return page.CutPosition;
        };
    }

    internal static async Task BoundsAsync(long before, long cut, long after)
    {
        await Assert.That(cut).IsGreaterThanOrEqualTo(before);
        await Assert.That(cut).IsLessThanOrEqualTo(after);
    }

    private static async Task OwnerAsync(NodeStatus actual, NodeStatus owner, CommitReceipt receipt)
    {
        await Assert.That(actual.NodeId).IsEqualTo(owner.NodeId);
        await Assert.That(actual.ProcessId).IsEqualTo(owner.ProcessId);
        await Assert.That(actual.Incarnation).IsEqualTo(owner.Incarnation);
        await Assert.That(actual.Incarnation).IsEqualTo(receipt.Token.Incarnation);
        await Assert.That(actual.RoutingReady).IsTrue();
        await Assert.That(actual.SiloCount).IsEqualTo(SiloCount);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(owner.ReadGeneration);
    }
}
