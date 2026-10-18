using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, QueryRuleOptimizationClusterState state,
        Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsClusterCallers root, QueryRuleOptimizationClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        foreach (var original in state.Originals)
        {
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(original.Command, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(original.Receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
                    original.Command, token))).Value), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsClusterCallers root, QueryRuleOptimizationClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var collection in QueryRuleOptimizationClusterData.Collections)
        {
            foreach (var row in QueryRuleOptimizationClusterData.Rows)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var reference = new EntityRef(state.Partition, collection, row.Id);
                    var expected = new DocumentReadResult(new(reference, row.Revision, row.Json, false, []));
                    await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)));
                    await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                        await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
                }, failures);
            }
        }
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
