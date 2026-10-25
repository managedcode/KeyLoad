using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventByteBoundPublicCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin,
        EventByteBoundPublicState state, CancellationToken token)
    {
        var request = EventByteBoundPublicExpected.Whole(state);
        await EventByteBoundPublicExpected.PageAsync(state,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(request, token)), [0, 1, 2], false);
        await EventByteBoundPublicExpected.PageAsync(state,
            (await McpCallerAssertions.SuccessAsync<StreamPage>(await admin.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value,
            [0, 1, 2], false);
        await SqlClusterProtocol.EqualAsync(state.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
