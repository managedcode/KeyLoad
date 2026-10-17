using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSchemaCursorPublicSeed
{
    private const string AppendKind = "appendEvents";
    internal static async Task<EventSchemaCursorPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            scenario.Stream.StreamSet, Capability.EventsRead, token);
        var initial = new ResourceDefinition(scenario.Stream.StreamSet, ResourceKind.StreamSet,
            scenario.Partition.TransactionDomainId);
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, placement,
            [new(AppendKind, scenario.Stream.StreamSet, scenario.Stream.StreamId, 3L)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(new(scenario.Stream, Limit: 3), token));
        await EventSchemaCursorPublicExpected.OriginalAsync(scenario, original);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventSchemaCursorPublicState(scenario, reader, placement, initial, command, receipt, original, outbox);
        await EventSchemaCursorPublicCurrent.ObserveAsync(admin, state, token);
        return state;
    }
}
