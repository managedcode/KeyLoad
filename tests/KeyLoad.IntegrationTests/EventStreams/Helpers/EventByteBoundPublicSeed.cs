using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventByteBoundPublicSeed
{
    private const string AppendKind = "appendEvents";
    internal static async Task<EventByteBoundPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            scenario.Stream.StreamSet, Capability.EventsRead, token);
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, placement,
            [new(AppendKind, scenario.Stream.StreamSet, scenario.Stream.StreamId, 3L)]);
        var original = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(new(scenario.Stream, Limit: 3), token));
        await EventByteBoundPublicExpected.OriginalAsync(scenario, original);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventByteBoundPublicState(scenario, reader, placement, command, receipt, original, outbox);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        return state;
    }
}
