using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

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
        var failures = new List<Exception>();
        foreach (var collection in new[] { SparseCompositePrefixClusterData.Only, SparseCompositePrefixClusterData.Fallback })
        {
            foreach (var row in SparseCompositePrefixClusterData.Rows)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var reference = new EntityRef(state.Partition, collection, row.Id);
                    var expected = new DocumentResult(reference, 1L, row.Json, false, []);
                    await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                        await root.Sdk.GetAsync(reference, state.Receipt.Token, token)));
                    await SqlClusterProtocol.EqualAsync(new DocumentReadResult(expected),
                        (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await root.Mcp.CallAsync(
                            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
                }, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task PageAsync(QueryPage actual, int shape, string path)
    {
        await SqlClusterProtocol.EqualAsync(new QueryPage([.. SparseCompositePrefixClusterData.Expected(shape)], null,
            actual.CutPosition, path), actual);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
    }
}
