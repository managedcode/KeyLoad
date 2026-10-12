using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireAssertions
{
    private const int Version = 1;
    private const int SourceFamily = 0;
    private const long InitialCleanupGeneration = 0;
    private const long PlacementStep = 1;

    internal static ControlledPartitionMovementNativeRetireCut Cut(ControlledPartitionMovementNativeHosts owners)
        => new(owners.Nodes.Select(owner => ControlledPartitionMovementRawImage.Bytes(owner.Store)).ToArray(),
            owners.Nodes.Select(owner => owner.Store.Position).ToArray(),
            owners.Nodes.Select(owner => owner.Log.State.LastIndex).ToArray());

    internal static async Task UnchangedAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCut before)
    {
        await Assert.That(owners.Nodes.Count).IsEqualTo(before.Rows.Length);
        for (var index = SourceFamily; index < owners.Nodes.Count; index++)
        {
            await Assert.That(owners.Nodes[index].Store.Position).IsEqualTo(before.Positions[index]);
            await Assert.That(owners.Nodes[index].Log.State.LastIndex).IsEqualTo(before.ReplicaTails[index]);
            await Assert.That(ControlledPartitionMovementRawImage.Bytes(owners.Nodes[index].Store)
                .SequenceEqual(before.Rows[index], StringComparer.Ordinal)).IsTrue();
        }
    }

    internal static async Task<PartitionMoveResult> PendingAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCaptured captured, PartitionMoveRequest request, long generation, CancellationToken cancellationToken)
    {
        var original = captured.Operation;
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original);
        var admission = phase.ReceiverEffectAdmission
            ?? throw new InvalidOperationException("The original Retire receiver admission is absent.");
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        var publication = body.Publication
            ?? throw new InvalidOperationException("The original Retire publication is absent.");
        var receiver = owners.Nodes[captured.OwnerIndex];
        _ = receiver.Database.VerifyOperationAuthority(original);
        await Assert.That(original.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
        await Assert.That(original.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(request,
            PhysicalShardCatalogFixture.RootPrincipalId, PartitionMovementParentPhaseRole.Retire,
            cleanupGeneration: generation));
        await Assert.That(phase.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(body.Role).IsEqualTo(PartitionMoveCleanupRole.Source);
        await Assert.That(body.FamilyOrdinal).IsEqualTo(SourceFamily);
        await Assert.That(body.Control.Phase).IsEqualTo(PartitionMovePhase.Published);
        await Assert.That(body.Control.SourceCut).IsGreaterThan(InitialCleanupGeneration);
        await Assert.That(JsonDefaults.Serialize(publication.Placement).AsSpan().SequenceEqual(JsonDefaults.Serialize(new AtomicPartitionPlacementV1(Version,
            request.Partition, owners.Destination.Owner.PhysicalShardId,
            request.ExpectedPlacementRevision + PlacementStep, owners.Destination.Owner.Incarnation,
            owners.Destination.Owner.VoterIds, phase.SourcePlacement.PlacementEpoch + PlacementStep)))).IsTrue();
        var retained = receiver.Store.Read(view => PartitionMoveParentStorage.Phase(view,
            request.Partition, request.MoveId, original.Id, receiver.Database.Limits.MaxBatchBytes))
            ?? throw new InvalidOperationException("The genuine pending Retire phase is not retained.");
        await Assert.That(retained.OriginalPhaseCommandId).IsEqualTo(original.Id);
        await Assert.That(retained.Stage).IsEqualTo(PartitionMovePeerStage.Retire);
        await Assert.That(retained.CleanupGeneration).IsEqualTo(generation);
        await Assert.That(retained.OriginalRequestNonce).IsEqualTo(admission.OriginalRequestNonce);
        await Assert.That(retained.OriginalExpiresAt).IsEqualTo(admission.OriginalExpiresAt);
        await Assert.That(retained.OriginalResult).IsNull();
        await Assert.That(retained.ObservationCheckpointReceipt).IsNull();
        var selected = receiver.Store.Read(view => CommandOutcomeKeyResolver.Select(view, original.PrincipalId,
            original.Id, new(CommandOutcomeScopeKind.Partition, request.Partition)));
        await Assert.That(selected.Outcome).IsNull();
        await ControlledPartitionMovementNativeRetireInstallReceipt.RequireAsync(owners, request,
            publication.Installed, cancellationToken);
        return new(request.MoveId, request.Partition, PartitionMovePhase.Retired, owners.Control.Owner,
            owners.Destination.Owner, body.Control.SourceCut, publication.Installed, publication.Placement);
    }
}
