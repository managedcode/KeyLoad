using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CanonicalApplyNetworkCleanup
{
    internal static async Task JoinAsync(RequestCqrsProbeFixture? controls,
        IReadOnlyList<ReplicaSiloDiscovery>? discovery, Guid armId, List<Exception> failures)
    {
        if (controls is null || discovery is null || armId == Guid.Empty)
        { return; }
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        ServerFailureObserver.Observe(controls.StopAdmission, failures);
        await ServerFailureObserver.ObserveAsync(() => controls.ReleaseOpenArmsAsync(discovery, deadline.Token), failures);
        if (controls.ArmFor(armId).RequestId is null || controls.ArmFor(armId).CanonicalOwnerDisposedSeen)
        { return; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.CanonicalOwnerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, deadline.Token);
        }, failures);
    }
}
