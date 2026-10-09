using KeyLoad.Client;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Native ingress holds reveal no payload and never change original first-voter dispatch.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3Producer
{
    private const string MissingIssue = "The genuine first receiver issuance ACK was not observed.";
    private const string MissingObservation = "The genuine receiver issuance proof ACK was not observed.";
    private const string FaultReason = "parent-original-receiver-issue-read-failover";

    internal static async Task<Result<PartitionMoveResult>> ExecuteAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, bool allReceivers, CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var acknowledged = Arm(wave, seed, RequestCqrsProbePhase.ParentReceiverIssueAcknowledged);
        var observed = Guid.Empty;
        using var originalCaller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var issue = wave.QueryControls.WaitForMarkerAsync(acknowledged,
            RequestCqrsProbePhase.ParentReceiverIssueAcknowledged, RequestCqrsProbeOutcome.Observed,
            discovery, originalCaller.Token);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest, originalCaller.Token);
        Result<PartitionMoveResult>? terminal = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(issue, call).ConfigureAwait(false);
            if (!issue.IsCompletedSuccessfully)
            { throw new InvalidOperationException(MissingIssue); }
            var marker = await issue.ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, acknowledged, seed.FirstRequest.MoveId,
                RequestCqrsProbePhase.ParentReceiverIssueAcknowledged, discovery).ConfigureAwait(false);
            if (!allReceivers)
            {
                observed = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
                    seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentReceiverIssueObserved,
                    RequestCqrsProbeAction.Hold, sourceRequestId: marker.RequestId, sourceArmId: acknowledged);
            }
            foreach (var node in Receivers(allReceivers))
            { await wave.RemoteRuntime.KillAsync(node, FaultReason, cancellationToken).ConfigureAwait(false); }
            wave.QueryControls.WriteRelease(acknowledged, marker.RequestId);
            if (!allReceivers)
            { await ReleaseObservedAsync(wave, seed, observed, discovery, call, cancellationToken).ConfigureAwait(false); }
            terminal = await call.ConfigureAwait(false);
            if (allReceivers)
            { await RequireNoObservedMarkerAsync(wave, seed.FirstRequest.MoveId); }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(originalCaller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await issue.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        // Retire the linked arm first; its actual primary remains active until both producers are joined.
        if (observed != Guid.Empty)
        { await JoinArmAsync(wave, observed, discovery, failures, cancellationToken).ConfigureAwait(false); }
        await JoinArmAsync(wave, acknowledged, discovery, failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return terminal ?? throw new InvalidOperationException(MissingObservation);
    }

    private static Guid Arm(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        RequestCqrsProbePhase phase)
        => wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, phase, RequestCqrsProbeAction.Hold);

    private static IEnumerable<string> Receivers(bool allReceivers)
        => TwoRf3MembershipProtocol.Nodes.Skip(TwoRf3MembershipProtocol.MembersPerGroup)
            .Take(allReceivers ? TwoRf3MembershipProtocol.MembersPerGroup : 1);

    private static async Task ReleaseObservedAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        Guid arm, IReadOnlyList<ReplicaSiloDiscovery> discovery, Task<Result<PartitionMoveResult>> call,
        CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observation = wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentReceiverIssueObserved,
            RequestCqrsProbeOutcome.Observed, discovery, wait.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(observation, call).ConfigureAwait(false);
            if (!observation.IsCompletedSuccessfully)
            { throw new InvalidOperationException(MissingObservation); }
            var marker = await observation.ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, arm, seed.FirstRequest.MoveId,
                RequestCqrsProbePhase.ParentReceiverIssueObserved, discovery).ConfigureAwait(false);
            // The single effect still targets original node4 under its unchanged original expiry.
            await wave.RemoteRuntime.RestartAsync(TwoRf3MembershipProtocol.Node4,
                cancellationToken).ConfigureAwait(false);
            wave.QueryControls.WriteRelease(arm, marker.RequestId);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(wait.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinObservationWaitAsync(observation, wait.Token),
            failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task JoinObservationWaitAsync(Task<RequestCqrsProbeMarkerRecord> observation,
        CancellationToken ownedWait)
    {
        try
        { _ = await observation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (ownedWait.IsCancellationRequested) { }
    }

    private static async Task JoinArmAsync(TwoRf3MembershipWave wave, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, List<Exception> failures, CancellationToken cancellationToken)
    {
        if (wave.QueryControls.ArmFor(arm).RequestId is { } request)
        {
            await ServerFailureObserver.ObserveAsync(() => RequestCqrsPhaseFaultAssertions.VerifySettledAsync(
                wave.QueryControls, arm, request, wave.QueryControls.ArmFor(arm).CommandId,
                discovery, cancellationToken), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
    }
    private static async Task RequireNoObservedMarkerAsync(TwoRf3MembershipWave wave, Guid command)
    {
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = wave.QueryControls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            foreach (var path in RequestCqrsProbeFileValidation.ValidateContents(owned.Directory)
                .Where(path => Path.GetFileName(path).StartsWith(RequestCqrsProbeFixtureProtocol.MarkerFilePrefix,
                    StringComparison.Ordinal)))
            {
                var marker = wave.QueryControls.Json.ReadMarker(RequestCqrsProbeFileStore.ReadRecord(path));
                await Assert.That(marker.CommandId == command && marker.Phase == RequestCqrsProbePhase.ParentReceiverIssueObserved
                    && marker.Outcome == RequestCqrsProbeOutcome.Observed).IsFalse();
            }
        }
    }

}
