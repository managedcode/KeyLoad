using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only real released and linked observed predecessors establish the native retirement boundary.</summary>
internal static class PartitionMovementCleanupMatrixRetiredRf3Producer
{
    internal static async Task<PartitionMovementCleanupMatrixRf3Fault[]> ExecuteAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var controls = wave.QueryControls;
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var other = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            Guid.NewGuid(), null, RequestCqrsProbePhase.ParentFinalInstallPreflight, RequestCqrsProbeAction.Hold);
        var arm = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waits = new List<Task<RequestCqrsProbeMarkerRecord>>();
        var observed = controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged,
            RequestCqrsProbeOutcome.Observed, discovery, caller.Token);
        waits.Add(observed);
        var original = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        var faults = new List<PartitionMovementCleanupMatrixRf3Fault>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var primary = await RequireMarkerAsync(observed, original).ConfigureAwait(false);
            await controls.RetireArmAsync(other, caller.Token).ConfigureAwait(false);
            await RequireOriginalAbsenceSnapshotAsync(controls, other);
            var adjunct = controls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
                seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueObserved,
                RequestCqrsProbeAction.Hold, sourceRequestId: primary.RequestId, sourceArmId: primary.ArmId);
            controls.WriteRelease(arm, primary.RequestId);
            var released = controls.WaitForMarkerAsync(arm, primary.Phase,
                RequestCqrsProbeOutcome.Released, discovery, caller.Token);
            waits.Add(released);
            var actualRelease = await RequireMarkerAsync(released, original).ConfigureAwait(false);
            var linked = controls.WaitForMarkerAsync(adjunct, RequestCqrsProbePhase.ParentReceiverIssueObserved,
                RequestCqrsProbeOutcome.Observed, discovery, caller.Token);
            waits.Add(linked);
            var actualAdjunct = await RequireMarkerAsync(linked, original).ConfigureAwait(false);
            await RequireSameOwnerAsync(primary, actualRelease, actualAdjunct);
            await RequireOriginalAbsenceSnapshotAsync(controls, other);
            if (original.IsCompleted)
            { throw new InvalidOperationException(PartitionMovementActiveAdjunctProtocol.EarlyTerminal); }
            PartitionMovementCleanupMatrixRf3Fault.ResurrectRetired(controls, other, actualAdjunct, faults);
            var refused = await original.ConfigureAwait(false);
            await Assert.That(refused.IsFailed).IsTrue();
            await Assert.That(refused.Value).IsNull();
            await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
            await PartitionMovementCleanupMatrixRf3Assertions.RequireNoOriginalDisposalAsync(controls, actualAdjunct);
            await RequirePrimaryRefusalAsync(controls, primary);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        foreach (var wait in waits)
        { await ServerFailureObserver.ObserveAsync(() => JoinWaitAsync(wait, caller.Token), failures).ConfigureAwait(false); }
        await ServerFailureObserver.ObserveAsync(async () => { _ = await original.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        controls.StopAdmission();
        controls.RetainEvidence();
        ServerFailureObserver.ThrowIfAny(failures);
        if (faults.Count != PartitionMovementCleanupMatrixProtocol.SelectedControlVoterCount)
        { throw new InvalidOperationException(PartitionMovementCleanupMatrixProtocol.MissingRefusal); }
        return faults.ToArray();
    }

    private static async Task<RequestCqrsProbeMarkerRecord> RequireMarkerAsync(
        Task<RequestCqrsProbeMarkerRecord> marker, Task original)
    {
        _ = await Task.WhenAny(marker, original).ConfigureAwait(false);
        if (!marker.IsCompletedSuccessfully || original.IsCompleted)
        { throw new InvalidOperationException(PartitionMovementActiveAdjunctProtocol.EarlyTerminal); }
        return await marker.ConfigureAwait(false);
    }

    private static async Task RequireSameOwnerAsync(RequestCqrsProbeMarkerRecord primary,
        RequestCqrsProbeMarkerRecord released, RequestCqrsProbeMarkerRecord adjunct)
    {
        await Assert.That(released.RequestId).IsEqualTo(primary.RequestId);
        await Assert.That(released.Voter).IsEqualTo(primary.Voter);
        await Assert.That(adjunct.RequestId).IsEqualTo(primary.RequestId);
        await Assert.That(adjunct.CommandId).IsEqualTo(primary.CommandId);
        await Assert.That(adjunct.Voter).IsEqualTo(primary.Voter);
        await Assert.That(adjunct.SiloAddress).IsEqualTo(primary.SiloAddress);
    }

    private static async Task RequireOriginalAbsenceSnapshotAsync(RequestCqrsProbeFixture controls, Guid retiredOther)
    {
        var name = RequestCqrsProbeFileNames.Arm(retiredOther);
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = controls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var originalSnapshot = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory);
            await Assert.That(originalSnapshot.Any(path => Path.GetFileName(path) == name)).IsFalse();
        }
    }

    private static async Task RequirePrimaryRefusalAsync(RequestCqrsProbeFixture controls,
        RequestCqrsProbeMarkerRecord primary)
    {
        var state = controls.ArmFor(primary.ArmId);
        await Assert.That(state.Settled).IsTrue();
        await Assert.That(state.ProducerDisposedSeen).IsFalse();
        await Assert.That(state.DisposedGateJoined).IsFalse();
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name =>
            RequestCqrsProbeFileNames.OriginForNode(name) == primary.Voter);
        var owned = controls.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        foreach (var path in RequestCqrsProbeFileValidation.ValidateContents(owned.Directory).Where(path =>
            Path.GetFileName(path).StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix, StringComparison.Ordinal)))
        {
            var actual = controls.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
            if (actual.ArmId == primary.ArmId)
            { await Assert.That(actual.Phase == RequestCqrsProbePhase.ProducerDisposed).IsFalse(); }
        }
    }

    private static async Task JoinWaitAsync(Task<RequestCqrsProbeMarkerRecord> wait, CancellationToken originalWait)
    {
        try
        { _ = await wait.ConfigureAwait(false); }
        catch (OperationCanceledException) when (originalWait.IsCancellationRequested) { }
    }
}
