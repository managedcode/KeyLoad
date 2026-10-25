using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterRows
{
    private const string Point = "point";
    private const string PublicAliasPrefix = "{\"odd.name\":\"";
    private const string StringSuffix = "\"}";
    private const string ProtectedAliasPrefix = "{\"exposed\":\"";

    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, CancellationToken token)
    {
        var state = history.State;
        var expected = new DocumentResult(state.First, history.Revision, raw ? history.RawJson : history.SafeJson,
            !raw, raw ? [] : SafeProjectorOperationClusterProtocol.RedactedPaths);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync<DocumentResult?>(expected,
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(state.First, token)));
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.First), token);
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Key, SafeProjectorOperationClusterProtocol.Canary);
            }
        }, token);
        for (var selection = SafeProjectorOperationClusterRequests.StarSelection;
            selection < SafeProjectorOperationClusterRequests.SelectionCount; selection++)
        {
            await SelectionAsync(root, reader, history, raw, selection, token);
        }
    }

    private static async Task SelectionAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, int selection, CancellationToken token)
    {
        var requests = SafeProjectorOperationClusterRequests.Selection(history.State, selection);
        foreach (var ast in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var actual = ast ? await reader.Sdk.QueryAstAsync(requests.Ast, token) : await reader.Sdk.QueryAsync(requests.Sql, token);
                await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, raw, selection);
            }, token);
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    ast ? (object)requests.Ast : requests.Sql, token);
                await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, raw, selection);
                if (!raw)
                {
                    await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationClusterProtocol.Canary);
                }
            }, token);
        }
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationClusterHistory history,
        bool raw, int selection)
    {
        var json = selection switch
        {
            SafeProjectorOperationClusterRequests.StarSelection => raw ? history.RawJson : history.SafeJson,
            SafeProjectorOperationClusterRequests.ProtectedSelection => raw
                ? ProtectedAliasPrefix + SafeProjectorOperationClusterProtocol.Canary + StringSuffix : SafeProjectorOperationClusterProtocol.TypedNullJson,
            SafeProjectorOperationClusterRequests.PublicAliasSelection => PublicAliasPrefix + history.Status + StringSuffix,
            SafeProjectorOperationClusterRequests.NullSelection => SafeProjectorOperationClusterProtocol.TypedNullJson,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
        var paths = raw ? ImmutableArray<string>.Empty
            : selection == SafeProjectorOperationClusterRequests.StarSelection ? SafeProjectorOperationClusterProtocol.RedactedPaths
            : selection == SafeProjectorOperationClusterRequests.ProtectedSelection ? [SafeProjectorOperationClusterProtocol.LiteralPath] : [];
        await SqlClusterProtocol.EqualAsync(new QueryPage(
            [new(SafeProjectorOperationClusterProtocol.First, history.Revision, json, !paths.IsEmpty, paths)], null, actual.CutPosition, Point), actual);
    }
}
