using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, StrongReadSessionState state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsClusterCallers root, StrongReadSessionState state, CancellationToken token)
    {
        var rows = new List<QueryRow> { new(SentinelId, CreatedRevision, SentinelJson, RedactedFields: []) };
        if (state.Target is { } target)
        {
            rows.Add(new(TargetId, CreatedRevision, TargetJson, RedactedFields: []));
            await SqlClusterProtocol.EqualAsync(new DocumentResult(new(state.Partition, Collection, TargetId),
                CreatedRevision, TargetJson, false, []), target);
        }
        var request = new AstQueryRequest(state.Partition,
            new(Collection, null, [new(Star, Star)], null, [new(IdPath, false)], InventoryBound), AllowFullScan: true);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(request, token)), rows.ToArray());
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(
            McpCallerTools.QueryAst, request, token))).Value, rows.ToArray());
        await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token));
    }
    private static async Task PageAsync(QueryPage page, QueryRow[] expected)
    {
        await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(InvalidCut);
    }
}
