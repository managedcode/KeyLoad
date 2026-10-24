using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using P = KeyLoad.IntegrationTests.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.IntegrationTests.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SchemaVisibleProjectionClusterValues
{
    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, bool mixed, CancellationToken token)
    {
        await SchemaVisibleProjectionClusterChanges.WaveAsync(root, reader, history, raw, mixed, token);
        var paths = raw ? ImmutableArray<string>.Empty : mixed ? [S.NestedPath] : ImmutableArray<string>.Empty;
        var expected = new DocumentResult(history.State.First, history.Revision,
            raw ? history.RawJson : history.SafeJson, !raw, paths);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync<DocumentResult?>(expected,
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token)));
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(history.State.First), token);
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            }
        }, token);
        var requests = SafeProjectorOperationClusterRequests.Selection(history.State, SafeProjectorOperationClusterRequests.StarSelection);
        foreach (var ast in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var actual = ast ? await reader.Sdk.QueryAstAsync(requests.Ast, token) : await reader.Sdk.QueryAsync(requests.Sql, token);
                await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, expected);
            }, token);
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    ast ? (object)requests.Ast : requests.Sql, token);
                await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, expected);
                if (!raw)
                {
                    await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
                }
            }, token);
        }
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationClusterHistory history, DocumentResult expected)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(S.PositivePublicPosition);
        await SqlClusterProtocol.EqualAsync(new QueryPage([new(S.First, history.Revision, expected.Json, expected.Redacted,
            expected.RedactedFields)], null, actual.CutPosition, P.Point), actual);
    }
}
