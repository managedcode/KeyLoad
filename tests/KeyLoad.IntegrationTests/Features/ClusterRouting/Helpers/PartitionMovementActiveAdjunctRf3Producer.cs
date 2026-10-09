using KeyLoad.Client;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The real original failed SDK producer and exact disposal marker independently establish owner cleanup.</summary>
internal static class PartitionMovementActiveAdjunctRf3Producer
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        bool duplicate, CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged,
            RequestCqrsProbeAction.Hold);
        using var originalCaller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var marker = wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged,
            RequestCqrsProbeOutcome.Observed, discovery, originalCaller.Token);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest, originalCaller.Token);
        Guid[] faults = [];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(marker, call).ConfigureAwait(false);
            if (!marker.IsCompletedSuccessfully)
            { throw new InvalidOperationException(PartitionMovementActiveAdjunctProtocol.EarlyTerminal); }
            var primary = await marker.ConfigureAwait(false);
            faults = PartitionMovementActiveAdjunctRf3Fault.Create(wave, seed, primary, duplicate);
            var failed = await call.ConfigureAwait(false);
            await Assert.That(failed.IsFailed).IsTrue();
            await Assert.That(failed.Value).IsNull();
            await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
            var disposed = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
            await Assert.That(disposed.RequestId).IsEqualTo(primary.RequestId);
            await Assert.That(disposed.CommandId).IsEqualTo(primary.CommandId);
            await RequireNoFabricatedSettlementAsync(wave.QueryControls, arm, faults).ConfigureAwait(false);
            wave.QueryControls.JoinDisposedGate(arm, call);
            await Assert.That(wave.QueryControls.ArmFor(arm).DisposedGateJoined).IsTrue();
            await Assert.That(wave.QueryControls.ArmFor(arm).Settled).IsFalse();
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(originalCaller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinMarkerAsync(marker, originalCaller.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => PartitionMovementActiveAdjunctRf3Fault.RemoveAsync(wave, faults,
            cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireNoFabricatedSettlementAsync(RequestCqrsProbeFixture fixture, Guid primary,
        IEnumerable<Guid> faults)
    {
        var unclaimed = faults.ToHashSet();
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = fixture.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var paths = RequestCqrsProbeFileValidation.ValidateContents(owned.Directory)
                .Where(path => Path.GetFileName(path).StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix,
                    StringComparison.Ordinal));
            foreach (var path in paths)
            {
                var marker = fixture.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
                await Assert.That(unclaimed.Contains(marker.ArmId)).IsFalse();
                if (marker.ArmId == primary)
                { await Assert.That(marker.Outcome is RequestCqrsProbeOutcome.Cancelled or RequestCqrsProbeOutcome.Released).IsFalse(); }
            }
        }
        foreach (var fault in unclaimed)
        {
            await Assert.That(fixture.ArmFor(fault).RequestId).IsNull();
            await Assert.That(fixture.ArmFor(fault).MarkerRecordCount).IsEqualTo(NoMarkers);
        }
    }

    private static async Task JoinMarkerAsync(Task<RequestCqrsProbeMarkerRecord> marker, CancellationToken originalWait)
    {
        try
        { _ = await marker.ConfigureAwait(false); }
        catch (OperationCanceledException) when (originalWait.IsCancellationRequested) { }
    }
    private const int NoMarkers = 0;
}
