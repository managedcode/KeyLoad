using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Query;
using P = KeyLoad.ClusterTests.Authorization.SchemaVisibleProjectionClusterProtocol;
using S = KeyLoad.ClusterTests.Authorization.SafeProjectorOperationClusterProtocol;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SchemaVisibleProjectionClusterRoot
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

    internal static async Task ConfigureAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
        ResourceDefinition previous, CancellationToken token)
    {
        var resource = previous with
        {
            SchemaVersion = RootVersion,
            FieldPolicies = [new(RootPath, S.Classification, RawReadGrant: S.ReadGrant,
                RawUseGrant: S.UseGrant, SchemaReadGrant: P.SchemaGrant),
                new(S.StatusPath, S.Classification, RawReadGrant: S.ReadGrant, RawUseGrant: S.UseGrant)]
        };
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(history.State.Partition.TenantId,
                    history.State.Partition.DatabaseId, resource)
                {
                    ExpectedSchemaVersion = previous.SchemaVersion
                }, token)));
        }, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, P.RootEpoch, [], token);
    }

    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        await SchemaVisibleProjectionClusterChanges.WaveAsync(root, reader, history, false, false, token, rootPayload: true);
        var state = history.State;
        var star = SafeProjectorOperationClusterRequests.Selection(state, SafeProjectorOperationClusterRequests.StarSelection);
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
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SqlClusterProtocol.EqualAsync<DocumentResult?>(new(state.First, history.Revision, EmptyJson, true, []),
                await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(state.First, token)));
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.First), token);
            await SqlClusterProtocol.EqualAsync(new DocumentResult(state.First, history.Revision, EmptyJson, true, []),
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
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.QueryAstAsync(ordinary, token), ErrorCode.Validation, P.BindingDetail), token);
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, ordinary, token),
                    ErrorCode.Validation, state), token);
        }
    }

    private static async Task PageAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, QueryRequest sql, AstQueryRequest ast, string json, bool redacted, CancellationToken token)
    {
        foreach (var useAst in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var actual = await McpCallerAssertions.SdkSuccessAsync(useAst ? await reader.Sdk.QueryAstAsync(ast, token)
                    : await reader.Sdk.QueryAsync(sql, token));
                await EqualAsync(actual, history, json, redacted);
            }, token);
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var reply = await reader.Mcp.CallAsync(useAst ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                    useAst ? (object)ast : sql, token);
                await EqualAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, json, redacted);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, S.Canary);
            }, token);
        }
    }

    private static Task EqualAsync(QueryPage actual, SafeProjectorOperationClusterHistory history, string json, bool redacted)
        => SqlClusterProtocol.EqualAsync(new QueryPage([new(S.First, history.Revision, json, redacted, [])],
            null, actual.CutPosition, P.Point), actual);
}
