using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class AtomicMembershipScopeTrial
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers admin,
        RequestCqrsRf3Callers caller, RequestCqrsPhaseFaultIdentity identity, CancellationToken token)
    {
        var partition = identity.Partition;
        var original = new PartitionRef(partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey);
        await Assert.That(original.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        var placement = await TenantPackingActivationRf3Admission.ReadAsync(admin, partition, token);
        var owners = await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token);
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        var first = AtomicMembershipScopeCommands.Create(original, 0, AtomicMembershipScopeCommands.InitialJson);
        var firstReceipt = await AtomicMembershipScopeReplies.CommitAsync(caller, first, placement, 1, token);
        var firstOutbox = await AtomicMembershipScopeReplies.AdmitAsync(admin, partition, baseline, token);
        await AtomicMembershipScopeCurrent.RunAsync(admin, caller, identity, placement,
            1, AtomicMembershipScopeCommands.InitialJson, firstOutbox, token);
        var second = AtomicMembershipScopeCommands.Create(original, 1, AtomicMembershipScopeCommands.UpdatedJson);
        CommitReceipt? secondReceipt = null;
        OutboxStatus? secondOutbox = null;
        await AtomicMembershipScopeForeignOwners.DuringAsync(wave, async () =>
        {
            secondReceipt = await AtomicMembershipScopeReplies.CommitAsync(caller, second, placement, 2, token);
            secondOutbox = await AtomicMembershipScopeReplies.AdmitAsync(admin, partition, firstOutbox, token);
            await AtomicMembershipScopeCurrent.RunAsync(admin, caller, identity, placement,
                2, AtomicMembershipScopeCommands.UpdatedJson, secondOutbox, token);
        }, token);
        await NativeActivationMigrationRf3Ownership.RequirePhysicalAsync(owners,
            await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token));
        var state = new AtomicMembershipScopeState(identity, placement, owners, first, firstReceipt,
            second, secondReceipt!, secondOutbox!);
        await AtomicMembershipScopeReplay.RunAsync(admin, caller, state, token);
        await AtomicMembershipScopeCold.RunAsync(wave, state, token);
    }
}
