using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionLineageR41Refusal
{
    internal static async Task BothAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
     RequestCqrsClusterCallers reader, EventProjectionLineageR41State state, CancellationToken token)
    {
        foreach (var command in new[] { state.Projection, state.FreshProjection })
        {
            var result = await worker.Sdk.CommitAsync(command, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.NotFound, EventProjectionLineageR41Protocol.Unavailable), result.Problem);
            await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
            var response = await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
            _ = (await McpCallerAssertions.ErrorAsync(response, ErrorCode.NotFound, dispatched: true)).Value;
            await Assert.That(response.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
             .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(EventProjectionLineageR41Protocol.McpUnavailable);
            await EventProjectionLineageR41Canonical.CurrentAsync(admin, reader, state, token);
        }
    }
}
