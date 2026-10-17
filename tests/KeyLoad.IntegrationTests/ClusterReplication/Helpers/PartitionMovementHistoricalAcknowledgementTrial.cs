using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Original native ACK survives a full cold cohort restart before parent proof observation.</summary>
internal static class PartitionMovementHistoricalAcknowledgementTrial
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        await InterruptAsync(wave, seed, discovery, cancellationToken);
        var stageId = PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            stageId, cancellationToken);
        var original = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(before);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireUnknownAsync(before, original, observed: false);
        await Assert.That(before.Count(value => value.OriginalReceiverIssuance is not null))
            .IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        var current = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave, cancellationToken);
        await PartitionMovementHistoricalPacketAssertions.RestartedAsync(discovery, current, original);
        var refusal = await seed.Source.MovePartitionAsync(seed.FirstRequest with
        {
            Mode = PartitionMoveMode.Resume
        },
            cancellationToken);
        await Assert.That(refusal.IsFailed).IsTrue();
        await Assert.That(refusal.Value).IsNull();
        await Assert.That(refusal.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            stageId, cancellationToken);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireObservationOnlyAsync(original, before, after);
        await PartitionMovementHistoricalPacketAssertions.ExactAsync(original,
            PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(after));
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        var aborted = await PartitionMovementTransferCloseRf3AbortAssertions.AbortAsync(seed, cancellationToken);
        var terminal = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            stageId, cancellationToken);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireAbortDispositionAsync(terminal, original, aborted);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await PartitionMovementTransferCloseRf3Healthy.RequireAsync(wave, seed, aborted, cancellationToken);
    }

    private static async Task InterruptAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        IReadOnlyList<KeyLoad.Clustering.ReplicaSiloDiscovery> discovery, CancellationToken cancellationToken)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var phase = RequestCqrsProbePhase.ParentReceiverIssueAcknowledged;
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, phase, RequestCqrsProbeAction.Hold);
        var failures = new List<Exception>();
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var marker = await wave.QueryControls.WaitForMarkerAsync(arm, phase,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, seed.FirstRequest.MoveId, phase, discovery);
            await caller.CancelAsync();
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm,
                marker.RequestId, seed.FirstRequest.MoveId, discovery, cancellationToken);
            var failed = await call;
            await Assert.That(failed.IsFailed).IsTrue();
            await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
        }, failures);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call; }, failures);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm, cancellationToken), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
