using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupMatrixRf3Producer
{
    internal static async Task<PartitionMovementCleanupMatrixRf3Fault[]> ExecuteAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementCleanupMatrixFaultRole role, CancellationToken cancellationToken)
    {
        var controls = wave.QueryControls;
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var other = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            Guid.NewGuid(), null, RequestCqrsProbePhase.ParentFinalInstallPreflight, RequestCqrsProbeAction.Hold);
        var arm = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observed = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged,
            RequestCqrsProbeOutcome.Observed, discovery, caller.Token);
        var original = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        var faults = new List<PartitionMovementCleanupMatrixRf3Fault>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(observed, original).ConfigureAwait(false);
            if (!observed.IsCompletedSuccessfully)
            { throw new InvalidOperationException(PartitionMovementActiveAdjunctProtocol.EarlyTerminal); }
            var primary = await observed.ConfigureAwait(false);
            if (role == PartitionMovementCleanupMatrixFaultRole.RetainedObservedControl)
            { PartitionMovementCleanupMatrixRf3Fault.RenameObservedControl(controls, primary, faults); }
            else
            { PartitionMovementCleanupMatrixRf3Fault.RenameAdmittedArm(controls, SelectArm(role, other, primary), primary, faults); }
            var refused = await original.ConfigureAwait(false);
            await Assert.That(refused.IsFailed).IsTrue();
            await Assert.That(refused.Value).IsNull();
            await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
            await PartitionMovementCleanupMatrixRf3Assertions.RequireNoOriginalDisposalAsync(controls, primary);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinObservedAsync(observed, caller.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await original.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        controls.StopAdmission();
        controls.RetainEvidence();
        ServerFailureObserver.ThrowIfAny(failures);
        if (faults.Count != (role == PartitionMovementCleanupMatrixFaultRole.RetainedObservedControl
            ? PartitionMovementCleanupMatrixProtocol.SelectedControlVoterCount
            : RequestCqrsProbeFixtureProtocol.Nodes.Count))
        { throw new InvalidOperationException(PartitionMovementCleanupMatrixProtocol.MissingRefusal); }
        return faults.ToArray();
    }
    private static Guid SelectArm(PartitionMovementCleanupMatrixFaultRole role, Guid other,
        RequestCqrsProbeMarkerRecord primary) => role switch
        {
            PartitionMovementCleanupMatrixFaultRole.OtherKnownArm => other,
            PartitionMovementCleanupMatrixFaultRole.ClaimedOwnerArm => primary.ArmId,
            _ => throw new InvalidOperationException(PartitionMovementCleanupMatrixProtocol.FaultMismatch),
        };
    private static async Task JoinObservedAsync(Task<RequestCqrsProbeMarkerRecord> observed, CancellationToken originalWait)
    {
        try
        { _ = await observed.ConfigureAwait(false); }
        catch (OperationCanceledException) when (originalWait.IsCancellationRequested) { }
    }
}
