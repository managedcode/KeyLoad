using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountRf3MinimumToken
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers caller, SqlFetchCountRf3State state, CancellationToken token)
    {
        foreach (var row in SqlFetchCountRf3State.Rows)
        {
            var reference = new EntityRef(PostgresRf3State.Partition, SqlFetchCountRf3Protocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference, SqlFetchCountRf3Protocol.SeedRevision, row.Json, false, []));
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(reference, state.Receipt.Token, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value;
            await SqlRf3Protocol.EqualAsync(expected, sdk);
            await SqlRf3Protocol.EqualAsync(expected, mcp);
        }
    }
}
