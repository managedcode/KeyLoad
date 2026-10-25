using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.ClientApi;

internal static class GuideLifecyclePublicPreparation
{
    internal const string InitialJson = "{\"guide\":\"original-public-control\"}";
    internal const string HealthyJson = "{\"guide\":\"repaired-public-control\"}";

    internal static async Task<GuideLifecyclePublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, token);
        var seed = scenario.Command(Guid.NewGuid(), InitialJson);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(seed, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, seed, [new("putDocument", McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, 1L)]);
        var healthy = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, HealthyJson, 1L)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new GuideLifecyclePublicState(scenario, identity, seed, receipt, healthy, outbox);
        await GuideLifecyclePublicValues.CurrentAsync(administrator, state, token);
        return state;
    }
}
