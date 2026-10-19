using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochClusterSetup
{
    internal static async Task<InitialPrincipalEpochClusterState> RunAsync(RequestCqrsClusterCallers root, long epoch, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var partition = new PartitionRef("epoch-tenant-" + suffix, "epoch-database", "epoch-domain", "epoch-partition-" + suffix);
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(InitialPrincipalEpochClusterState.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)), token));
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
        return new(partition, id, "epoch-key-" + suffix, seed, receipt, Guid.NewGuid(), refused);
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
