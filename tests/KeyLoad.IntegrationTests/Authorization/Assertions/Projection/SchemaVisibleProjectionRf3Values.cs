using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Values
{
    internal static async Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, bool mixed, CancellationToken token)
    {
        await SchemaVisibleProjectionRf3Changes.WaveAsync(root, reader, history, raw, mixed, token);
        var paths = raw ? ImmutableArray<string>.Empty : mixed ? [S.NestedPath] : ImmutableArray<string>.Empty;
        var expected = new DocumentResult(history.State.First, history.Revision,
            raw ? history.RawJson : history.SafeJson, !raw, paths);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(expected,
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token)));
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(history.State.First), token);
            await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
            if (!raw)
            {
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            }
        }, token);
        var requests = SafeProjectorOperationRf3Requests.Selection(history.State, SafeProjectorOperationRf3Requests.StarSelection);
        foreach (var ast in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var actual = ast ? await reader.Sdk.QueryAstAsync(requests.Ast, token) : await reader.Sdk.QueryAsync(requests.Sql, token);
                await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, expected);
            }, token);
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
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

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationRf3History history, DocumentResult expected)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(S.PositivePublicPosition);
        await SqlRf3Protocol.EqualAsync(new QueryPage([new(S.First, history.Revision, expected.Json, expected.Redacted,
            expected.RedactedFields)], null, actual.CutPosition, P.Point), actual);
    }
}
