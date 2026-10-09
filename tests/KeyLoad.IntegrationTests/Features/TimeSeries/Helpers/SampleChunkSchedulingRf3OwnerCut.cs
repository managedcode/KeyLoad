using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3OwnerCut
{
    private const string Scenario = "sample-chunk-original-provider-abrupt-owner";

    internal static async Task KillAndJoinAsync(TwoRf3MembershipWave wave,
        DistributedApplication originalOwner, IReadOnlyList<Guid> originalArms, CancellationToken token)
    {
        var failures = new List<Exception>();
        // The original owner registry is intentionally mutated serially; every accepted native kill settles.
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node, Scenario, token), failures)
                .ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        { _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false); }, failures).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => wave.QueryControls.JoinKilledOwnerGates(wave,
                originalOwner, originalArms), failures);
            foreach (var arm in originalArms)
            {
                await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm, token), failures)
                    .ConfigureAwait(false);
            }
        }
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(() => SampleChunkSchedulingRf3KillEvidence.WriteAsync(
                wave.RemoteRuntime.RequireKilledOwners(originalOwner)), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
