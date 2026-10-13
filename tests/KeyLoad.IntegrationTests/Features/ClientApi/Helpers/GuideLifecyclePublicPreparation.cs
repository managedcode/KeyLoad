using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class GuideLifecyclePublicPreparation
{
    internal const string InitialJson = "{\"guide\":\"original-public-control\"}";
    internal const string HealthyJson = "{\"guide\":\"repaired-public-control\"}";

    internal static async Task<GuideLifecyclePublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, token);
        var request = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        var seed = scenario.Command(Guid.NewGuid(), InitialJson);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(seed, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, seed, placement,
            [new("putDocument", McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, 1L)]);
        var healthy = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, HealthyJson, 1L)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new GuideLifecyclePublicState(scenario, identity, placement, seed, receipt, healthy, outbox);
        await GuideLifecyclePublicValues.CurrentAsync(administrator, state, token);
        return state;
    }
}
