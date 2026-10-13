using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3FaultOwner
{
    private const int FirstEntry = 0;
    private const long RevisionStep = 1;
    private const string Scenario = "whole-partition-committed-prepare";

    internal static async Task KillAndJoinAsync(TwoRf3MembershipWave wave, string observedNode,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(observedNode, Scenario, token), failures);
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Where(node => node != observedNode))
        { await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node, Scenario, token), failures); }
        await wave.DisposeApplicationAsync(failures);
        if (wave.CanCheckLocks)
        { ServerFailureObserver.Observe(wave.AssertAllNodeLocksReleased, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<ReplicaSiloDiscovery[]> DiscoveryAsync(TwoRf3MembershipWave wave,
        CancellationToken token)
    {
        var actual = new List<ReplicaSiloDiscovery>();
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node2,
            TwoRf3MembershipProtocol.Node3 })
        {
            actual.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                node, wave.Profile, token));
        }
        return actual.ToArray();
    }

    internal static async Task ResumeAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests, CancellationToken token)
    {
        await wave.RestartJoinedAsync(token);
        await wave.WaitForSixHealthyAsync(token);
        foreach (var seed in seeds)
        { await seed.ReconnectAsync(wave, token); }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var settled = await PhysicalShardPartitionMovementRf3Flow.ExecuteAsync(seeds, requests, token);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], requests[index],
                seeds[index].OriginalPlacement, seeds[index].Directory.ControlOwner, settled[index],
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + index + RevisionStep),
                checked(seeds[0].OriginalPlacement.DirectoryRevision + requests.Length), token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        await PhysicalShardPartitionMovementRf3Cold.ReplayAsync(wave, seeds, requests, settled, token);
        await PhysicalShardPartitionMovementRf3Healthy.RequireAsync(wave, seeds, requests, settled, token);
        await PhysicalShardPartitionMovementRf3Flow.MergeAsync(wave, seeds, token);
    }
}
