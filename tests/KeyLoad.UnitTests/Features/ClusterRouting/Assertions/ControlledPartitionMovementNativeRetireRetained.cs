using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireRetained
{
    internal static byte[] Read(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCaptured captured, PartitionMoveRequest request)
    {
        var owner = owners.Nodes[captured.OwnerIndex];
        return owner.Store.Read(view => NativeSerialization.Serialize(PartitionMoveParentStorage.Phase(view,
            request.Partition, request.MoveId, captured.Operation.Id, owner.Database.Limits.MaxBatchBytes)
            ?? throw new InvalidOperationException("The original sealed Retire phase is not retained.")));
    }

    internal static async Task ColdAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCaptured captured, PartitionMoveRequest request, byte[] original)
    {
        await Assert.That(Read(owners, captured, request).SequenceEqual(original)).IsTrue();
        var retained = NativeSerialization.Deserialize<PartitionMoveParentPhase>(original);
        await Assert.That(retained.OriginalPhaseCommandId).IsEqualTo(captured.Operation.Id);
        await Assert.That(retained.OriginalResult).IsNull();
        await Assert.That(retained.ObservationCheckpointReceipt).IsNull();
        var owner = owners.Nodes[captured.OwnerIndex];
        var selection = owner.Store.Read(view => CommandOutcomeKeyResolver.Select(view,
            captured.Operation.PrincipalId, captured.Operation.Id,
            new(CommandOutcomeScopeKind.Partition, request.Partition)));
        await Assert.That(selection.Outcome).IsNull();
        var actualPrincipal = owner.Store.Read(view => owner.Database.Principal(view,
            PhysicalShardCatalogFixture.RootPrincipalId, owner.Clock.GetUtcNow()));
        await Assert.That(actualPrincipal.Id).IsEqualTo(captured.Operation.PrincipalId);
        await Assert.That(actualPrincipal.ClusterAdministrator).IsTrue();
    }
}
