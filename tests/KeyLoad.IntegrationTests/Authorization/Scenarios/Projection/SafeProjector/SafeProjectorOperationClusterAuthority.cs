using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterAuthority
{
    private const string Point = "point";
    private const string FullScan = "bounded-full-scan";

    internal static async Task SetAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
        long epoch, ImmutableArray<string> grants, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var principal = history.State.Principal(epoch, grants);
        await SafeProjectorOperationClusterImages.MutationAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(id, principal, token)));
        }, token);
        history.PrincipalCommands.Add((id, principal));
    }

    internal static async Task UsageAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool allowed, bool raw, CancellationToken token)
    {
        foreach (var order in new[] { false, true })
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                {
                    var actual = ast
                        ? await reader.Sdk.QueryAstAsync(SafeProjectorOperationClusterRequests.UseAst(history.State, order), token)
                        : await reader.Sdk.QueryAsync(SafeProjectorOperationClusterRequests.UseSql(history.State, order), token);
                    if (!allowed)
                    {
                        await SafeProjectorOperationClusterErrors.SdkAsync(actual, ErrorCode.PermissionDenied, SafeProjectorOperationClusterProtocol.UseDenied);
                        return;
                    }
                    await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, raw, order ? Point : FullScan);
                }, token);
                await McpUsageAsync(root, reader, history, order, ast, allowed, raw, token);
            }
        }
    }

    private static async Task McpUsageAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool order, bool ast, bool allowed, bool raw, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                ast ? (object)SafeProjectorOperationClusterRequests.UseAst(history.State, order)
                    : SafeProjectorOperationClusterRequests.UseSql(history.State, order), token);
            if (!allowed)
            {
                await SafeProjectorOperationClusterErrors.McpAsync(reply, ErrorCode.PermissionDenied, history.State);
                return;
            }
            await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value,
                history, raw, order ? Point : FullScan);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationClusterProtocol.Canary);
            }
        }, token);
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationClusterHistory history, bool raw, string access)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
        var paths = raw ? ImmutableArray<string>.Empty : SafeProjectorOperationClusterProtocol.RedactedPaths;
        await SqlClusterProtocol.EqualAsync(new QueryPage(
            [new(SafeProjectorOperationClusterProtocol.First, history.Revision, raw ? history.RawJson : history.SafeJson, !raw, paths)],
            null, actual.CutPosition, access), actual);
    }
}
