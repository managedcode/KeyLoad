using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Settled actual page ACKs precede the first final authorization; no final authority exists in the original cut.</summary>
internal static class PartitionMovementFinalInstallFrameRf3Preparation
{
    private const int PrecedingOrdinal = 1;

    internal static async Task<PartitionMovementFinalInstallFramePrecut> CaptureAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var owners = await PartitionMovementFinalInstallFrameRf3Inspection.CaptureOwnersAsync(wave, cancellationToken);
        await PartitionMovementFinalInstallFrameRf3Producer.HoldPreFinalAsync(wave, seed, cancellationToken);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, cancellationToken);
        var control = cuts.First(cut => cut.Header is not null);
        var state = PartitionMovementParentCapacityRf3Snapshot.Read(wave, control.Node, seed, seed.FirstRequest,
            new DatabaseLimits().MaxBatchBytes, cancellationToken);
        var descriptor = state.Selected!.OriginalDescriptor!;
        var total = descriptor.Families.Sum(static family => family.PageCount);
        await Assert.That(total).IsGreaterThan(PartitionMoveProtocol.EmptyCount);
        var grantId = PartitionMovementParentPhaseIds.For(seed.FirstRequest,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.InstallGrant, total);
        var effectId = PartitionMovementParentPhaseIds.For(seed.FirstRequest,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.Install, total);
        foreach (var cut in cuts.Where(cut => cut.Header is not null))
        {
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.LastIssued!.Stage).IsEqualTo(PartitionMovePeerStage.ControlAcknowledge);
            await Assert.That(cut.LastIssued.OriginalResult!.Error).IsNull();
            await Assert.That(cut.LastIssued.ObservationCheckpointReceipt).IsNotNull();
            await Assert.That(cut.LastIssued.OriginalResult.Get<PartitionMovePhaseResult>().Grant!.Stage)
                .IsEqualTo(PartitionMovePeerStage.Install);
            await Assert.That(cut.LastIssued.OriginalResult.Get<PartitionMovePhaseResult>().Grant!.PageOrdinal)
                .IsEqualTo(total - PrecedingOrdinal);
            await SqlRf3Protocol.EqualAsync(control.LastIssued, cut.LastIssued);
            await Assert.That(PartitionMovementParentCapacityRf3Snapshot.ReadGrant(wave, cut.Node,
                seed.FirstRequest, grantId, new DatabaseLimits().MaxBatchBytes)).IsNull();
        }
        var calibrated = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, cuts, cancellationToken);
        var refused = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, cuts, cancellationToken);
        return new(cuts, owners, state, total, grantId, effectId, calibrated, refused);
    }
}

internal sealed record PartitionMovementFinalInstallFramePrecut(PartitionMovementPublicParentRf3NativeCut[] Cuts,
    Dictionary<string, NodeStatus> Owners, PartitionMoveParentState State, int FinalOrdinal, Guid GrantId, Guid EffectId,
    PartitionMovementStoppedNativeSnapshot Calibrated, PartitionMovementStoppedNativeSnapshot Refused);
