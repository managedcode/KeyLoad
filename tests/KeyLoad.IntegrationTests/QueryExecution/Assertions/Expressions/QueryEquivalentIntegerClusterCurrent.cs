using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, QueryEquivalentIntegerClusterState state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsClusterCallers root, QueryEquivalentIntegerClusterState state, CancellationToken token)
    {
        var request = new AstQueryRequest(state.Partition,
            new(Collection, null, [new(Star, Star)], null, [new(IdPath, false)], Limit), AllowFullScan: true);
        QueryRow[] expected = [.. Records.Select(row => new QueryRow(row.Id, Revision, row.Json, RedactedFields: []))];
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(request, token)), expected);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await root.Mcp.CallAsync(
            McpCallerTools.QueryAst, request, token))).Value, expected);
    }
    internal static async Task PageAsync(QueryPage page, QueryRow[] expected)
    {
        await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, QueryEquivalentIntegerClusterState state, CancellationToken token)
    {
        await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
        }, token);
        await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
    }
}
