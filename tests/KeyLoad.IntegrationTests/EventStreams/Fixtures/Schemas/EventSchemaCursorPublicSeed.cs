using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventSchemaCursorPublicSeed
{
    private const string AppendKind = "appendEvents";
    internal static async Task<EventSchemaCursorPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            scenario.Stream.StreamSet, Capability.EventsRead, token);
        var initial = new ResourceDefinition(scenario.Stream.StreamSet, ResourceKind.StreamSet,
            scenario.Partition.TransactionDomainId);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(AppendKind, scenario.Stream.StreamSet, scenario.Stream.StreamId, 3L)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(new(scenario.Stream, Limit: 3), token));
        await EventSchemaCursorPublicExpected.OriginalAsync(scenario, original);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventSchemaCursorPublicState(scenario, reader, initial, command, receipt, original, outbox);
        await EventSchemaCursorPublicCurrent.ObserveAsync(admin, state, token);
        return state;
    }
}
