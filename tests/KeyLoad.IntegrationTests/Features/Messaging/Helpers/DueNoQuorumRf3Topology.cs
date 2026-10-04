using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3Topology
{
    internal static async Task<DueNoQuorumRf3FaultPlan> CaptureAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        var leaders = status.Select(item => item.Status.Leader).Where(item => item is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1 || status.Any(item => item.Status.Leader != leaders[0]))
        { throw new InvalidOperationException(DueNoQuorumRf3Protocol.LeaderFailure); }
        var leader = status.Single(item => item.Admin.LocalVoter == leaders[0]).Name;
        if (dueAt - TimeProvider.System.GetUtcNow() < TimeSpan.FromSeconds(DueNoQuorumRf3Protocol.MinimumSetupLeadSeconds))
        { throw new TimeoutException(DueNoQuorumRf3Protocol.SetupFailure); }
        var ordered = new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 };
        var recovery = ordered.First(item => item != leader);
        var survivor = ordered.Single(item => item != leader && item != recovery);
        if (new[] { leader, recovery, survivor }.Distinct(StringComparer.Ordinal).Count() != RequestCqrsRf3Protocol.NodeCount)
        { throw new InvalidOperationException(DueNoQuorumRf3Protocol.VoterFailure); }
        return new(leader, recovery, survivor, status, discovery);
    }

    internal static async Task AssertOneRestartedAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, DueNoQuorumRf3FaultPlan plan, CancellationToken cancellationToken)
    {
        var before = plan.BeforeDiscovery.Single(item => item.VoterId == VoterOrigin(plan.RecoveryNode));
        var survivorBefore = plan.BeforeDiscovery.Single(item => item.VoterId == VoterOrigin(plan.Survivor));
        var restarted = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, plan.RecoveryNode, profile,
            cancellationToken).ConfigureAwait(false);
        var survivor = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, plan.Survivor, profile,
            cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiscoveryOracle.AssertCurrentAsync(restarted).ConfigureAwait(false);
        await RequestCqrsRf3DiscoveryOracle.AssertCurrentAsync(survivor).ConfigureAwait(false);
        await AssertSameIdentityAsync(before, restarted).ConfigureAwait(false);
        await AssertSameIdentityAsync(survivorBefore, survivor).ConfigureAwait(false);
        await Assert.That(restarted.SiloAddress).IsNotEqualTo(before.SiloAddress);
        await Assert.That(survivor.SiloAddress).IsEqualTo(survivorBefore.SiloAddress);
    }

    internal static async Task AssertFinalDiscoveryAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        DueNoQuorumRf3FaultPlan plan, CancellationToken cancellationToken)
    {
        var after = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < after.Length; index++)
        {
            var prior = plan.BeforeDiscovery[index];
            await AssertSameIdentityAsync(prior, after[index]).ConfigureAwait(false);
            var restarted = after[index].VoterId == VoterOrigin(plan.Leader)
                || after[index].VoterId == VoterOrigin(plan.RecoveryNode);
            await Assert.That(after[index].SiloAddress != prior.SiloAddress).IsEqualTo(restarted);
        }
    }

    private static async Task AssertSameIdentityAsync(ReplicaSiloDiscovery prior, ReplicaSiloDiscovery current)
    {
        await Assert.That(current.VoterId).IsEqualTo(prior.VoterId);
        await Assert.That(current.ClusterId).IsEqualTo(prior.ClusterId);
        await Assert.That(current.Incarnation).IsEqualTo(prior.Incarnation);
    }

    private static string VoterOrigin(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => "http://node1:8080",
        RequestCqrsRf3Protocol.Node2 => "http://node2:8080",
        RequestCqrsRf3Protocol.Node3 => "http://node3:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
