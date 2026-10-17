using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;
using P = KeyLoad.IntegrationTests.Features.Authorization.SchemaVisibleProjectionRf3Protocol;
using S = KeyLoad.IntegrationTests.Features.Authorization.SafeProjectorOperationRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SchemaVisibleProjectionRf3Root
{
    private const long RootVersion = 4;
    private const string RootPath = "";
    private const string Revision = "/@revision";
    private const string PayloadId = "/id";
    private const string PayloadRevision = "/revision";
    private const string EmptyJson = "{}";
    private const string IdJson = "{\"exposed\":\"a\"}";
    private const string Prefix = "SELECT d.id AS exposed FROM [";
    private const string Suffix = "] d WHERE d.id = 'a' ORDER BY d.revision";
    private const string StarPrefix = "SELECT * FROM [";

    internal static async Task ConfigureAsync(RequestCqrsRf3Callers root, SafeProjectorOperationRf3History history,
        ResourceDefinition previous, CancellationToken token)
    {
        var resource = previous with
        {
            SchemaVersion = RootVersion,
            FieldPolicies = [new(RootPath, S.Classification, RawReadGrant: S.ReadGrant,
                RawUseGrant: S.UseGrant, SchemaReadGrant: P.SchemaGrant),
                new(S.StatusPath, S.Classification, RawReadGrant: S.ReadGrant, RawUseGrant: S.UseGrant)]
        };
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(history.State.Partition.TenantId,
                    history.State.Partition.DatabaseId, resource)
                {
                    ExpectedSchemaVersion = previous.SchemaVersion
                }, token)));
        }, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, P.RootEpoch, [], token);
    }

    internal static async Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        await SchemaVisibleProjectionRf3Changes.WaveAsync(root, reader, history, false, false, token, rootPayload: true);
        var state = history.State;
        var star = SafeProjectorOperationRf3Requests.Selection(state, SafeProjectorOperationRf3Requests.StarSelection);
        var metadata = star.Ast with
        {
            Query = star.Ast.Query with
            {
                Projection = [new(S.IdPath, P.ValueAlias)],
                Order = [new(Revision, false)]
            }
        };
        await PageAsync(root, reader, history, new(state.Partition, Prefix + state.Collection + Suffix, AllowFullScan: true),
            metadata, IdJson, false, token);
        await PageAsync(root, reader, history,
            new(state.Partition, Prefix.Replace("d.id", "d.revision", StringComparison.Ordinal) + state.Collection + Suffix, AllowFullScan: true),
            metadata with
            {
                Query = metadata.Query with
                {
                    Projection = [new(Revision, P.ValueAlias)]
                }
            }, "{\"exposed\":2}", false, token);
        await PageAsync(root, reader, history,
            new(state.Partition, StarPrefix + state.Collection + Suffix, AllowFullScan: true),
            metadata with
            {
                Query = metadata.Query with
                {
                    Projection = [new(P.Star, P.Star)]
                }
            }, EmptyJson, true, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(new(state.First, history.Revision, EmptyJson, true, []),
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(state.First, token)));
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.First), token);
            await SqlRf3Protocol.EqualAsync(new DocumentResult(state.First, history.Revision, EmptyJson, true, []),
                (await PublicReadAssertions.RequiredDocumentAsync(reply)).Value);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Key, S.Canary);
        }, token);
        foreach (var path in new[] { PayloadId, PayloadRevision })
        {
            var ordinary = metadata with
            {
                Query = metadata.Query with
                {
                    Projection = [new(path, P.ValueAlias)]
                }
            };
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.QueryAstAsync(ordinary, token), ErrorCode.Validation, P.BindingDetail), token);
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ordinary, token),
                    ErrorCode.Validation, state), token);
        }
    }

    private static async Task PageAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, QueryRequest sql, AstQueryRequest ast, string json, bool redacted, CancellationToken token)
    {
        foreach (var useAst in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var actual = await McpCallerAssertions.SdkSuccessAsync(useAst ? await reader.Sdk.QueryAstAsync(ast, token)
                    : await reader.Sdk.QueryAsync(sql, token));
                await EqualAsync(actual, history, json, redacted);
            }, token);
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var reply = await reader.Mcp.CallAsync(useAst ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    useAst ? (object)ast : sql, token);
                await EqualAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, json, redacted);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            }, token);
        }
    }

    private static Task EqualAsync(QueryPage actual, SafeProjectorOperationRf3History history, string json, bool redacted)
        => SqlRf3Protocol.EqualAsync(new QueryPage([new(S.First, history.Revision, json, redacted, [])],
            null, actual.CutPosition, P.Point), actual);
}
