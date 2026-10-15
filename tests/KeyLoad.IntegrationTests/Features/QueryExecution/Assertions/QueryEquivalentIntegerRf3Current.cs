using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, QueryEquivalentIntegerRf3State state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsRf3Callers root, QueryEquivalentIntegerRf3State state, CancellationToken token)
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
        await SqlRf3Protocol.EqualAsync(expected, page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, QueryEquivalentIntegerRf3State state, CancellationToken token)
    {
        await ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
        }, token);
        await ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
    }
}
