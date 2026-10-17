using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Authority
{
    private const string Point = "point";
    private const string FullScan = "bounded-full-scan";

    internal static async Task SetAsync(RequestCqrsRf3Callers root, SafeProjectorOperationRf3History history,
        long epoch, ImmutableArray<string> grants, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var principal = history.State.Principal(epoch, grants);
        await SafeProjectorOperationRf3Images.MutationAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigurePrincipalAsync(id, principal, token)));
        }, token);
        history.PrincipalCommands.Add((id, principal));
    }

    internal static async Task UsageAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool allowed, bool raw, CancellationToken token)
    {
        foreach (var order in new[] { false, true })
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                {
                    var actual = ast
                        ? await reader.Sdk.QueryAstAsync(SafeProjectorOperationRf3Requests.UseAst(history.State, order), token)
                        : await reader.Sdk.QueryAsync(SafeProjectorOperationRf3Requests.UseSql(history.State, order), token);
                    if (!allowed)
                    {
                        await SafeProjectorOperationRf3Errors.SdkAsync(actual, ErrorCode.PermissionDenied, SafeProjectorOperationRf3Protocol.UseDenied);
                        return;
                    }
                    await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, raw, order ? Point : FullScan);
                }, token);
                await McpUsageAsync(root, reader, history, order, ast, allowed, raw, token);
            }
        }
    }

    private static async Task McpUsageAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool order, bool ast, bool allowed, bool raw, CancellationToken token)
    {
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                ast ? (object)SafeProjectorOperationRf3Requests.UseAst(history.State, order)
                    : SafeProjectorOperationRf3Requests.UseSql(history.State, order), token);
            if (!allowed)
            {
                await SafeProjectorOperationRf3Errors.McpAsync(reply, ErrorCode.PermissionDenied, history.State);
                return;
            }
            await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value,
                history, raw, order ? Point : FullScan);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationRf3Protocol.Canary);
            }
        }, token);
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationRf3History history, bool raw, string access)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
        var paths = raw ? ImmutableArray<string>.Empty : SafeProjectorOperationRf3Protocol.RedactedPaths;
        await SqlRf3Protocol.EqualAsync(new QueryPage(
            [new(SafeProjectorOperationRf3Protocol.First, history.Revision, raw ? history.RawJson : history.SafeJson, !raw, paths)],
            null, actual.CutPosition, access), actual);
    }
}
