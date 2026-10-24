using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SparseCompositePrefixClusterOperations
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SparseCompositePrefixClusterState state, bool denied, CancellationToken token)
    {
        foreach (var includeStatusIndex in new[] { false, true })
        {
            var collection = includeStatusIndex ? SparseCompositePrefixClusterData.WithStatusIndex : SparseCompositePrefixClusterData.Only;
            await CollectionAsync(root, reader, state, collection, includeStatusIndex, denied, token);
        }
    }
    private static async Task CollectionAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SparseCompositePrefixClusterState state, string collection, bool includeStatusIndex, bool denied, CancellationToken token)
    {
        foreach (var shape in new[] { 0, 1, 2 })
        {
            foreach (var reference in new[] { false, true })
            {
                var path = reference ? SparseCompositePrefixClusterData.Scan
                    : SparseCompositePrefixClusterData.Path(state.IncludeNull, state.IncludeMissing, includeStatusIndex, shape);
                await RoutesAsync(root, reader, state, collection, shape, reference, path, denied ? ErrorCode.PermissionDenied : null,
                    consent: true, token);
            }
        }
        if (!denied)
        {
            var indexed = state.IncludeNull && state.IncludeMissing || includeStatusIndex;
            await RoutesAsync(root, reader, state, collection, 0, false,
                SparseCompositePrefixClusterData.Path(state.IncludeNull, state.IncludeMissing, includeStatusIndex, 0),
                indexed ? null : ErrorCode.UnsupportedCapability, consent: false, token);
        }
    }
    private static async Task RoutesAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SparseCompositePrefixClusterState state, string collection, int shape, bool reference,
        string path, ErrorCode? refusal, bool consent, CancellationToken token)
    {
        var sql = SparseCompositePrefixClusterRequests.Sql(state.Partition, collection, shape, reference, consent);
        await SparseCompositePrefixClusterCurrent.ObserveAsync(root, state, async () =>
        {
            var actual = await reader.Sdk.QueryAsync(sql, token);
            if (refusal is { } error)
            {
                await Assert.That(actual.IsSuccess).IsFalse();
                await Assert.That(actual.Value).IsNull();
                await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(error.ToString());
            }
            else
            {
                await SparseCompositePrefixClusterCurrent.PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), shape, path);
            }
        }, token);
        await SparseCompositePrefixClusterCurrent.ObserveAsync(root, state, async () =>
        {
            var actual = await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token);
            if (refusal is { } error)
            {
                await McpCallerAssertions.ErrorAsync(actual, error, dispatched: true);
            }
            else
            {
                await SparseCompositePrefixClusterCurrent.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(actual)).Value, shape, path);
            }
        }, token);
        foreach (var ast in new[] { SparseCompositePrefixClusterRequests.Ast(state.Partition, collection, shape, reference, consent),
            SparseCompositePrefixClusterRequests.CSharp(state.Partition, collection, shape, reference, consent) })
        {
            await AstAsync(root, reader, state, ast, shape, path, refusal, token);
        }
    }
    private static async Task AstAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SparseCompositePrefixClusterState state, AstQueryRequest request, int shape, string path, ErrorCode? refusal, CancellationToken token)
    {
        await SparseCompositePrefixClusterCurrent.ObserveAsync(root, state, async () =>
        {
            var actual = await reader.Sdk.QueryAstAsync(request, token);
            if (refusal is { } error)
            {
                await Assert.That(actual.IsSuccess).IsFalse();
                await Assert.That(actual.Value).IsNull();
                await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(error.ToString());
            }
            else
            {
                await SparseCompositePrefixClusterCurrent.PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), shape, path);
            }
        }, token);
        await SparseCompositePrefixClusterCurrent.ObserveAsync(root, state, async () =>
        {
            var actual = await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token);
            if (refusal is { } error)
            {
                await McpCallerAssertions.ErrorAsync(actual, error, dispatched: true);
            }
            else
            {
                await SparseCompositePrefixClusterCurrent.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(actual)).Value, shape, path);
            }
        }, token);
    }
}
