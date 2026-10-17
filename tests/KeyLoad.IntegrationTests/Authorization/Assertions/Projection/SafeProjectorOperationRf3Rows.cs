using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Rows
{
    private const string Point = "point";
    private const string PublicAliasPrefix = "{\"odd.name\":\"";
    private const string StringSuffix = "\"}";
    private const string ProtectedAliasPrefix = "{\"exposed\":\"";

    internal static async Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, CancellationToken token)
    {
        var state = history.State;
        var expected = new DocumentResult(state.First, history.Revision, raw ? history.RawJson : history.SafeJson,
            !raw, raw ? [] : SafeProjectorOperationRf3Protocol.RedactedPaths);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(expected,
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(state.First, token)));
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.First), token);
            await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Key, SafeProjectorOperationRf3Protocol.Canary);
            }
        }, token);
        for (var selection = SafeProjectorOperationRf3Requests.StarSelection;
            selection < SafeProjectorOperationRf3Requests.SelectionCount; selection++)
        {
            await SelectionAsync(root, reader, history, raw, selection, token);
        }
    }

    private static async Task SelectionAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, int selection, CancellationToken token)
    {
        var requests = SafeProjectorOperationRf3Requests.Selection(history.State, selection);
        foreach (var ast in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var actual = ast ? await reader.Sdk.QueryAstAsync(requests.Ast, token) : await reader.Sdk.QueryAsync(requests.Sql, token);
                await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, raw, selection);
            }, token);
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    ast ? (object)requests.Ast : requests.Sql, token);
                await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, raw, selection);
                if (!raw)
                {
                    await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationRf3Protocol.Canary);
                }
            }, token);
        }
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationRf3History history,
        bool raw, int selection)
    {
        var json = selection switch
        {
            SafeProjectorOperationRf3Requests.StarSelection => raw ? history.RawJson : history.SafeJson,
            SafeProjectorOperationRf3Requests.ProtectedSelection => raw
                ? ProtectedAliasPrefix + SafeProjectorOperationRf3Protocol.Canary + StringSuffix : SafeProjectorOperationRf3Protocol.TypedNullJson,
            SafeProjectorOperationRf3Requests.PublicAliasSelection => PublicAliasPrefix + history.Status + StringSuffix,
            SafeProjectorOperationRf3Requests.NullSelection => SafeProjectorOperationRf3Protocol.TypedNullJson,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
        var paths = raw ? ImmutableArray<string>.Empty
            : selection == SafeProjectorOperationRf3Requests.StarSelection ? SafeProjectorOperationRf3Protocol.RedactedPaths
            : selection == SafeProjectorOperationRf3Requests.ProtectedSelection ? [SafeProjectorOperationRf3Protocol.LiteralPath] : [];
        await SqlRf3Protocol.EqualAsync(new QueryPage(
            [new(SafeProjectorOperationRf3Protocol.First, history.Revision, json, !paths.IsEmpty, paths)], null, actual.CutPosition, Point), actual);
    }
}
