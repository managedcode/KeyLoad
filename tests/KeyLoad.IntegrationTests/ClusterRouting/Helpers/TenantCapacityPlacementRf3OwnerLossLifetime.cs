using Aspire.Hosting;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OwnerLossLifetime
{
    internal static async Task JoinAsync(TwoRf3MembershipWave wave, DistributedApplication originalOwner,
        RequestCqrsProbeFixture controls, Guid arm, ReplicaSiloDiscovery[] discovery,
        Task<Result<PlacementIntentV1>> original, Task<RequestCqrsProbeMarkerRecord> marker,
        TenantCapacityPlacementRf3OwnerLossHeld.State? held, CancellationTokenSource caller,
        CancellationTokenSource observer, List<Exception> failures, CancellationToken token)
    {
        if (wave.applicationDisposed && wave.nodeLocksReleased && held is { } actual)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures);
            await ServerFailureObserver.ObserveAsync(() => marker, failures);
            ServerFailureObserver.Observe(() => controls.JoinKilledOwnerGates(wave, originalOwner,
                [arm], actual.Journal, actual.Marker, original), failures);
            return;
        }
        await TenantCapacityPlacementRf3ClaimLifetime.JoinAsync(controls, arm, discovery, original,
            marker, caller, observer, failures, token);
    }
}
