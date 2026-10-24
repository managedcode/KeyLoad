using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class MetamorphicPublicCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, MetamorphicPublicState state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsClusterCallers root, MetamorphicPublicState state, CancellationToken token)
    {
        var request = new AstQueryRequest(state.Partition, new(MetamorphicPublicData.Collection, null, [new("*", "*")], null,
            [new("/@id", false)], MetamorphicPublicData.Count), AllowFullScan: true);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(request, token));
        var expected = state.Rows.OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => new QueryRow(row.Id, 1L, row.Json, false, [])).ToArray();
        await MetamorphicPublicResults.PageAsync(actual, expected, "bounded-full-scan", true);
    }
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, MetamorphicPublicState state, CancellationToken token)
        => await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
}
