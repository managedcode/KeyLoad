using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveQueryCutRf3Assertions
{
    internal const string QueryTool = "keyload_query_ast";
    private const string FullScanPath = "bounded-full-scan";
    private const int VoterCount = 3;

    internal static async Task<Func<Task<long>>> CreateAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, CommitReceipt receipt,
        ImmutableArray<QueryRow> rows, Func<Task<QueryPage>> query, CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        return async () =>
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
            await OwnerAsync(before, owner, receipt);
            var document = await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.GetAsync(scenario.Reference, receipt.Token, token));
            DocumentResult? expected = rows.IsEmpty ? null : new(scenario.Reference,
                rows.Single().Revision, rows.Single().Json, rows.Single().Redacted,
                [FeedLiveRf3Protocol.SecretField]);
            await SqlRf3Protocol.EqualAsync(expected, document.Document);
            var page = await query();
            await SqlRf3Protocol.EqualAsync(new QueryPage(rows, null, page.CutPosition, FullScanPath), page);
            var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
            await OwnerAsync(after, owner, receipt);
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(before.Applied);
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
        await Assert.That(actual.Voters).IsEqualTo(VoterCount);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Applied).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await Assert.That(actual.ReadGeneration).IsGreaterThanOrEqualTo(owner.ReadGeneration);
    }
}
