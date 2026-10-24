using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventByteBoundPublicSeed
{
    private const string AppendKind = "appendEvents";
    internal static async Task<EventByteBoundPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            scenario.Stream.StreamSet, Capability.EventsRead, token);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(AppendKind, scenario.Stream.StreamSet, scenario.Stream.StreamId, 3L)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(new(scenario.Stream, Limit: 3), token));
        await EventByteBoundPublicExpected.OriginalAsync(scenario, original);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventByteBoundPublicState(scenario, reader, command, receipt, original, outbox);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        return state;
    }
}
