using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionLineageR41Pages
{
    internal static async Task BothAsync(RequestCqrsClusterCallers reader, EventProjectionLineageR41State state, Func<Task> observe, CancellationToken token)
    {
        var query = new SearchRequest(state.Partition, EventProjectionLineageR41Protocol.Collection,
         VectorField: EventProjectionLineageR41Protocol.Vector, Vector: state.Replaced ? [0f, 1f] : [1f, 0f],
         Space: EventProjectionLineageR41Protocol.Space, Limit: 2, FusionConstant: 60);
        var expected = state.Replaced ? new[] { Row(state, EventProjectionLineageR41Protocol.Baseline, 1d / 61d), Row(state, EventProjectionLineageR41Protocol.Target, 1d / 62d) }
         : state.Deleted ? new[] { Row(state, EventProjectionLineageR41Protocol.Baseline, 1d / 61d) }
         : new[] { Row(state, EventProjectionLineageR41Protocol.Target, 1d / 61d), Row(state, EventProjectionLineageR41Protocol.Baseline, 1d / 62d) };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.SearchAsync(query, token)));
        await observe();
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<RankedDocument[]>(
         await reader.Mcp.CallAsync(McpCallerTools.SearchExecute, query, token))).Value);
        await observe();
    }
    private static RankedDocument Row(EventProjectionLineageR41State state, string id, double score)
     => new(new(new(state.Partition, EventProjectionLineageR41Protocol.Collection, id), 1L,
      id == EventProjectionLineageR41Protocol.Target ? EventProjectionLineageR41Protocol.TargetJson : EventProjectionLineageR41Protocol.BaselineJson,
      true, [EventProjectionLineageR41Protocol.Input, EventProjectionLineageR41Protocol.Vector]), score);
}
