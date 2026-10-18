using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionGroupLeaderFailoverFlow
{
    private const int RequiredVoters = 3;
    private const int FirstNode = 0;
    private const string LeaderMissing = "The actual CLUSTER leader was not reported.";
    private const string LeaderLossReason = "subscription-gap-leader-loss";
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var statuses = new NodeStatus[SubscriptionFilterClusterProtocol.Nodes.Length];
        for (var index = FirstNode; index < statuses.Length; index++)
        {
            statuses[index] = ClusterReplicationTestSupport.Success(
                await fixture.Client(SubscriptionFilterClusterProtocol.Nodes[index]).StatusAsync(token));
            await Assert.That(statuses[index].Voters).IsEqualTo(RequiredVoters);
        }
        var originalLeader = new Uri(statuses[FirstNode].Leader
            ?? throw new InvalidOperationException(LeaderMissing));
        var leader = SubscriptionFilterClusterProtocol.Nodes.Single(node =>
            string.Equals(node, originalLeader.Host, StringComparison.Ordinal));
        var survivors = SubscriptionFilterClusterProtocol.Nodes.Where(node => node != leader).ToArray();
        var failures = new List<Exception>();
        var stopped = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = true;
            await fixture.KillContainerAsync(leader, LeaderLossReason, token);
            await AwaitElectionAsync(fixture, survivors, originalLeader.Host, statuses[FirstNode].ConsensusTerm, token);
            await SubscriptionGroupLeaderFailoverCallers.RunAsync(fixture, state, survivors, token);
        }, failures);
        if (stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(leader, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await SubscriptionIndependentCheckpointR52Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
    }

    private static Task AwaitElectionAsync(ClusterFixture fixture, string[] survivors, string oldLeader,
        long originalTerm, CancellationToken token)
        => ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            string? elected = null;
            foreach (var node in survivors)
            {
                var result = await fixture.Client(node).StatusAsync(token);
                if (!result.IsSuccess || result.Value is not { } status || !status.RoutingReady
                    || status.Voters != RequiredVoters || status.ConsensusTerm <= originalTerm || status.Leader is null)
                {
                    return false;
                }
                var host = new Uri(status.Leader).Host;
                if (host == oldLeader || elected is not null && host != elected)
                {
                    return false;
                }
                elected = host;
            }
            return elected is not null;
        }, token);
}
