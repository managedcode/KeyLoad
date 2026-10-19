using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class EventProjectionLineageR41Replay
{
    internal static async Task ProjectionAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
     RequestCqrsClusterCallers reader, EventProjectionLineageR41State state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Original, await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(state.Projection, token)));
        await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.Original, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
         await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Projection, token))).Value);
        await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
    }
    internal static async Task SuccessesAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
     EventProjectionLineageR41State state, CancellationToken token)
    {
        foreach (var row in state.Successes)
        {
            await SqlClusterProtocol.EqualAsync(row.Receipt, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(row.Request, token)));
            await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
            await SqlClusterProtocol.EqualAsync(row.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
             await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, row.Request, token))).Value);
            await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
        }
    }
}
