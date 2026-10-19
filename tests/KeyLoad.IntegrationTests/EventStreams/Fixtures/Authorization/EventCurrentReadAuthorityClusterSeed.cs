using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityClusterSeed
{
    internal static async Task<EventCurrentReadAuthorityClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            McpEventStreamTokens.StreamSet, Capability.EventsRead | Capability.EventsAppend, token);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(EventCurrentReadAuthorityClusterExpected.AppendKind,
                McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 3L)]);
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(new(scenario.Stream), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventCurrentReadAuthorityClusterState(scenario, identity, command, receipt, page, outbox);
        await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
            EventCurrentReadAuthorityClusterExpected.Data(false), 1L, 3L, null, false);
        await EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
        return state;
    }
}
