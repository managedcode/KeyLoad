using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Existing real request-grain callback owns expiry, caller cancellation and producer settlement.</summary>
internal static class PartitionMovementNativeSealedCallbacks
{
    internal static async Task InterruptAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        Guid commandId, IReadOnlyList<ReplicaSiloDiscovery> discovery, bool cancelCaller, CancellationToken token)
    {
        var phase = RequestCqrsProbePhase.RetireOperationSealed;
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            commandId, null, phase, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var request = cancelCaller ? seed.FirstRequest with
        {
            Mode = PartitionMoveMode.Resume
        } : seed.FirstRequest;
        var call = seed.Source.MovePartitionAsync(request, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var observed = await wave.QueryControls.WaitForMarkerAsync(arm, phase,
                RequestCqrsProbeOutcome.Observed, discovery, token);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(observed, arm, commandId, phase, discovery);
            if (cancelCaller)
            {
                await caller.CancelAsync();
            }
            var cancelled = await wave.QueryControls.WaitForMarkerAsync(arm, phase,
                RequestCqrsProbeOutcome.Cancelled, discovery, token);
            await Assert.That(cancelled.RequestId).IsEqualTo(observed.RequestId);
            await Assert.That(cancelled.CommandId).IsEqualTo(commandId);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm,
                observed.RequestId, commandId, discovery, token);
            var actual = await call;
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
        }, failures);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call; }, failures);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
