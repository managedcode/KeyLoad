using KeyLoad.IntegrationTests.DocumentStorage;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.ResourceExecution;

namespace KeyLoad.IntegrationTests.ClientApi;

internal static class SdkProblemBudgetPublicPreparation
{
    internal const string Entity = "budget-document";
    internal const string DeniedCollection = "budget-forbidden";
    internal const string AllowedJson = "{\"value\":\"allowed-original\"}";
    internal const string ForbiddenJson = "{\"value\":\"forbidden-original\"}";
    internal const string HealthyJson = "{\"value\":\"allowed-healthy\"}";
    private const string DeniedJson = "{\"value\":\"forbidden-change\"}";

    internal static async Task<SdkProblemBudgetPublicState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await McpDocumentScenario.CreateAsync(fixture, token);
        var resource = new ResourceDefinition(DeniedCollection, ResourceKind.Collection,
            scenario.Partition.TransactionDomainId);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, resource);
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigureResourceAsync(creationId, creation, token)));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, token);
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, Entity, AllowedJson, 0L),
             new PutDocument(DeniedCollection, Entity, ForbiddenJson, 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(seed, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, seed, [new("putDocument", McpDocumentProtocol.Collection, Entity, 1L), new("putDocument", DeniedCollection, Entity, 1L)]);
        var denied = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(DeniedCollection, Entity, DeniedJson, 1L)]);
        var healthy = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, Entity, HealthyJson, 1L)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var status = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        var state = new SdkProblemBudgetPublicState(identity, seed, receipt, denied, healthy, outbox, status,
            scenario.Lifetime, ResourceCreationLifetime.Create(creationId, creation));
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        return state;
    }
}
