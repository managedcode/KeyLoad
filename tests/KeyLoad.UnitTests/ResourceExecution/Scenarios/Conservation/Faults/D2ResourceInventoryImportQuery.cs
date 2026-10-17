using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportQuery
{
    internal static async Task RefusedAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        await D2ResourceInventoryRefusals.FailureAsync(() => Read(target, corpus, token), ErrorCode.RecoveryRequired);
        await before.AssertUnchangedAsync(target);
    }

    internal static async Task HealthyAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, ResourceLifetimeV1 lifetime, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var page = Read(target, corpus, token);
        await Assert.That(page.Continuation).IsNull();
        await Assert.That(page.Entries.Length).IsEqualTo(D2ResourceInventoryImportData.Single);
        var partition = D2ResourceInventoryImportData.Partition;
        var owner = corpus.Destination.Owner;
        var placement = new AtomicPartitionPlacementResolution(D2ResourceInventoryImportData.Version, partition,
            owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
            D2ResourceInventoryImportData.Empty, D2ResourceInventoryImportData.Empty, true);
        await D2ResourceInventoryAssertions.EqualAsync(new ResourceOwnerInventoryEntryV1(
            lifetime, partition, owner, placement, corpus.Destination), page.Entries.Single());
        await before.AssertUnchangedAsync(target);
    }

    private static ResourceOwnerInventoryPageV1 Read(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), target.Database.EvaluationClock, token);
        var partition = D2ResourceInventoryImportData.Partition;
        var request = target.Database.AdmitResourceOwnerInventoryRequest(PhysicalShardCatalogFixture.RootPrincipalId,
            Guid.NewGuid(), partition.TenantId, partition.DatabaseId, D2ResourceInventoryImportData.Resource,
            D2ResourceInventoryImportData.Single,
            target.Database.EvaluationClock.GetUtcNow().AddSeconds(target.Database.Limits.QueryDeadlineSeconds), work);
        var control = target.Database.CaptureResourceOwnerInventoryControl(PhysicalShardCatalogFixture.RootPrincipalId, request, work);
        var page = target.Database.ReadResourceOwnerInventoryPage(PhysicalShardCatalogFixture.RootPrincipalId,
            control, corpus.Destination, null, work);
        target.Database.ValidateResourceOwnerInventoryOwner(PhysicalShardCatalogFixture.RootPrincipalId, control, page.Cut, work);
        target.Database.ValidateResourceOwnerInventoryControl(PhysicalShardCatalogFixture.RootPrincipalId, control, work);
        return page;
    }
}
