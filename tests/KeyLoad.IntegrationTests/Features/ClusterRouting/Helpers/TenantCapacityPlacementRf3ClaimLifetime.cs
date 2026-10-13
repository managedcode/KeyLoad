using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ClaimLifetime
{
    internal static async Task JoinAsync(RequestCqrsProbeFixture controls, Guid arm,
        ReplicaSiloDiscovery[] discovery, Task<Result<PlacementIntentV1>> original,
        Task<RequestCqrsProbeMarkerRecord> marker, CancellationTokenSource originalCancellation,
        CancellationTokenSource markerCancellation, List<Exception> failures, CancellationToken token)
    {
        if (failures.Count != 0)
        {
            await ServerFailureObserver.ObserveAsync(originalCancellation.CancelAsync, failures);
            await ServerFailureObserver.ObserveAsync(markerCancellation.CancelAsync, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => marker, failures);
        var state = controls.ArmFor(arm);
        if (state.RequestId is { } request && !state.ReleaseWritten && !state.Settled)
        { ServerFailureObserver.Observe(() => controls.WriteRelease(arm, request), failures); }
        await ServerFailureObserver.ObserveAsync(() => original, failures);
        if (state.RequestId is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(() => controls.WaitForSettlementAsync(arm, discovery, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await controls.WaitForMarkerAsync(arm,
            RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed, discovery, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => controls.RetireArmAsync(arm, token), failures);
    }
}
