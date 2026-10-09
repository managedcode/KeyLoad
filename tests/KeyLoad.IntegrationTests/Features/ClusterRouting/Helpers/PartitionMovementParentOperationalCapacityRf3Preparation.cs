using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual settled pre-admission cohort and separately observed real later page are never merged as authority.</summary>
internal static class PartitionMovementParentOperationalCapacityRf3Preparation
{
    internal static async Task<PartitionMovementParentOperationalCapacityPrecut> CaptureAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, int legalLimit,
        CancellationToken cancellationToken)
    {
        await PartitionMovementParentOperationalCapacityRf3Producer.HoldPreflightAsync(wave, seed,
            cancellationToken).ConfigureAwait(false);
        var cuts = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        var controls = cuts.Where(cut => cut.Header is not null).ToArray();
        await Assert.That(controls.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        foreach (var cut in controls)
        {
            await Assert.That(cut.Pending).IsNull();
            await Assert.That(cut.Header!.OriginalCapturePhaseCommandId).IsNotNull();
            await Assert.That(cut.LastIssued!.Stage).IsEqualTo(PartitionMovePeerStage.ControlAdvance);
            await Assert.That(cut.LastIssued.OriginalResult!.Error).IsNull();
            await Assert.That(cut.LastIssued.ObservationCheckpointReceipt).IsNotNull();
        }
        var state = PartitionMovementParentCapacityRf3Snapshot.Read(wave, controls.First().Node, seed,
            seed.FirstRequest, legalLimit, cancellationToken);
        await Assert.That(state.Pending).IsNull();
        await Assert.That(state.Control!.Phase).IsEqualTo(PartitionMovePhase.Captured);
        await Assert.That(state.Selected!.Stage).IsEqualTo(PartitionMovePeerStage.Capture);
        await Assert.That(state.Selected.OriginalDescriptor).IsNotNull();
        await Assert.That(state.Selected.CaptureProofCheckpointReceipt).IsNotNull();
        await Assert.That(state.Selected.ObservationCheckpointReceipt).IsNotNull();
        var inspected = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, cuts, cancellationToken);
        var refused = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, cuts, cancellationToken);
        return new(cuts, state, inspected, refused);
    }

    internal static async Task<PartitionMoveAuthorizeBody> InspectPageAndRestoreAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementParentOperationalCapacityPrecut precut,
        CancellationToken cancellationToken)
    {
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Producer.HoldStageGrantAsync(wave, seed,
            cancellationToken).ConfigureAwait(false);
        var identity = PartitionMovementParentOperationalCapacityRf3Producer.StageGrantId(seed.FirstRequest);
        var observed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            identity, cancellationToken);
        var controls = observed.Where(cut => cut.Header is not null).ToArray();
        foreach (var cut in controls)
        {
            var pending = cut.Pending ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await Assert.That(pending.OriginalPhaseCommandId).IsEqualTo(identity);
            await Assert.That(pending.Stage).IsEqualTo(PartitionMovePeerStage.ControlAuthorize);
            await Assert.That(pending.OriginalGrant).IsNull();
            await Assert.That(pending.OriginalResult).IsNull();
        }
        var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(controls.First().Pending!.OriginalPhase!.Body.Span);
        foreach (var cut in controls)
        { await SqlRf3Protocol.EqualAsync(body, NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(cut.Pending!.OriginalPhase!.Body.Span)); }
        await precut.Inspected.RestoreAsync(wave, observed, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
        return body;
    }
}

internal sealed record PartitionMovementParentOperationalCapacityPrecut(PartitionMovementPublicParentRf3NativeCut[] Cuts,
    PartitionMoveParentState State, PartitionMovementStoppedNativeSnapshot Inspected,
    PartitionMovementStoppedNativeSnapshot Refused);
