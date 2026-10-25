using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SparseCompositePrefixClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, SparseCompositePrefixClusterState state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Receipt,
            await SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp, SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Seed) with
            {
                CommandId = state.Seed.CommandId
            }, token)), failures);
        await ServerFailureObserver.ObserveAsync(() => DocumentsAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DocumentsAsync(RequestCqrsClusterCallers root, SparseCompositePrefixClusterState state, CancellationToken token)
    {
        // The fourteen source reads are independent at one read cut, so they are issued together and every one is joined.
        var reads = new[] { SparseCompositePrefixClusterData.Only, SparseCompositePrefixClusterData.WithStatusIndex }
            .SelectMany(collection => SparseCompositePrefixClusterData.Rows.Select(row => DocumentAsync(root, state, collection, row, token)))
            .ToArray();
        var failures = new List<Exception>();
        foreach (var read in reads)
        {
            await ServerFailureObserver.ObserveAsync(() => read, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DocumentAsync(RequestCqrsClusterCallers root, SparseCompositePrefixClusterState state,
        string collection, (string Id, string Json) row, CancellationToken token)
    {
        var reference = new EntityRef(state.Partition, collection, row.Id);
        var expected = new DocumentResult(state.Stored(collection, row.Id), 1L, row.Json, false, []);
        await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
            await root.Sdk.GetAsync(reference, state.Receipt.Token, token)));
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected),
            (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await root.Mcp.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
    }
    internal static async Task PageAsync(QueryPage actual, int shape, string path)
    {
        await SqlClusterProtocol.EqualAsync(new QueryPage([.. SparseCompositePrefixClusterData.Expected(shape)], null,
            actual.CutPosition, path), actual);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
    }
}
