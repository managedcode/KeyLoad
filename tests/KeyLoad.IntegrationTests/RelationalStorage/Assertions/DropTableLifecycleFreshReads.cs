using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleFreshReads
{
    internal static async Task OldAbsentAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleTable table, CancellationToken token)
    {
        foreach (var original in table.Rows)
        {
            // A name-only read of a different absent ID is not an old-lifetime replay.
            var nameOnly = original.Reference with
            {
                Lifetime = null
            };
            var expected = new DocumentReadResult(null);
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.GetAsync(nameOnly, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(nameOnly), token))).Value);
        }
    }
}
