using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.ResourceExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class InitialPrincipalEpochClusterSetup
{
    internal static async Task<InitialPrincipalEpochClusterState> RunAsync(RequestCqrsClusterCallers root, long epoch, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var partition = new PartitionRef("epoch-tenant-" + suffix, "epoch-database", "epoch-domain", "epoch-partition-" + suffix);
        var creationId = Guid.NewGuid();
        var definition = new ResourceDefinition(InitialPrincipalEpochClusterState.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(creationId, creation, token));
        var seed = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(InitialPrincipalEpochClusterState.Collection, "record", "{\"value\":0}", 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await InitialPrincipalEpochClusterValues.ReceiptAsync(seed, receipt, 1L);
        var id = "epoch-reader-" + suffix;
        var refused = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, InitialPrincipalEpochClusterState.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            PolicyEpoch = epoch
        };
        return new(partition, id, "epoch-key-" + suffix, seed, receipt, Guid.NewGuid(), refused,
            ResourceCreationLifetime.Create(creationId, creation));
    }

    internal static async Task PrincipalAsync(RequestCqrsClusterCallers root, InitialPrincipalEpochClusterState state,
        long epoch, bool granted, bool revoked, CancellationToken token)
    {
        var expected = state.Principal(epoch, granted, revoked);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(epoch == 1L ? state.PrincipalCommandId : Guid.NewGuid(), expected, token)));
    }
    internal static async Task<Guid> KeyAsync(RequestCqrsClusterCallers root, InitialPrincipalEpochClusterState state,
        bool replacement, bool expired, CancellationToken token)
    {
        var id = Guid.NewGuid();
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(id,
            state.Credential(replacement, expired), token));
        return id;
    }
}
