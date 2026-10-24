using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlFetchCountClusterMinimumToken
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers caller, SqlFetchCountClusterState state, CancellationToken token)
    {
        foreach (var row in SqlFetchCountClusterState.Rows)
        {
            var reference = new EntityRef(PostgresClusterState.Partition, SqlFetchCountClusterProtocol.Collection, row.Id);
            var expected = new DocumentReadResult(new(reference, SqlFetchCountClusterProtocol.SeedRevision, row.Json, false, []));
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(reference, state.Receipt.Token, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value;
            await SqlClusterProtocol.EqualAsync(expected, sdk);
            await SqlClusterProtocol.EqualAsync(expected, mcp);
        }
    }
}
