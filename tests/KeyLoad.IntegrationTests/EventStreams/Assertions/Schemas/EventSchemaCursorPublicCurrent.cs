using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSchemaCursorPublicCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        var request = EventSchemaCursorPublicExpected.Whole(state);
        await EventSchemaCursorPublicExpected.PageAsync(state,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(request, token)), [0, 1, 2], false, false);
        await EventSchemaCursorPublicExpected.PageAsync(state,
            (await McpCallerAssertions.SuccessAsync<StreamPage>(await admin.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value,
            [0, 1, 2], false, false);
        await SqlClusterProtocol.EqualAsync(state.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }
}
