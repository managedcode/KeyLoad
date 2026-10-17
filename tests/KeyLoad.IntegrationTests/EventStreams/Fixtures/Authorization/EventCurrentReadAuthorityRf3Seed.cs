using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityRf3Seed
{
    internal static async Task<EventCurrentReadAuthorityRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            McpEventStreamTokens.StreamSet, Capability.EventsRead | Capability.EventsAppend, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
                new AtomicPartitionPlacementReadRequest(1, scenario.Partition), token));
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, placement,
            [new(EventCurrentReadAuthorityRf3Expected.AppendKind,
                McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 3L)]);
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(new(scenario.Stream), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventCurrentReadAuthorityRf3State(scenario, identity, placement, command, receipt, page, outbox);
        await EventCurrentReadAuthorityRf3Expected.PageAsync(state, page,
            EventCurrentReadAuthorityRf3Expected.Data(false), 1L, 3L, null, false);
        await EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token);
        return state;
    }
}
