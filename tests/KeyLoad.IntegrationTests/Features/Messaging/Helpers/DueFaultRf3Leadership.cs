using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3Leadership
{
    private const string LeaderMissing = "The healthy RF3 statuses did not identify one physical leader.";

    internal static async Task<DueFaultRf3LeaderCut> ExerciseLeaderLossAsync(DistributedApplication app,
        RequestCqrsRf3Wave wave, NodeEpochRf3Profile profile, DueFaultRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var beforeStatus = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        var beforeDiscovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        var leader = FindLeader(beforeStatus);
        DueFaultRf3SeedWriter.AssertSetupLead(seed.DueAt);
        var survivors = beforeStatus.Where(node => node.Name != leader.Name).Select(node => node.Name).ToArray();
        await wave.KillLeaderAsync(leader.Name, cancellationToken).ConfigureAwait(false);
        var cut = new DueFaultRf3LeaderCut(leader.Name, survivors, beforeStatus, beforeDiscovery, 0);
        await VerifySurvivorProgressAsync(app, profile, seed, cut, cancellationToken).ConfigureAwait(false);
        var requiredApplied = await ReadSurvivorHighWaterAsync(app, profile, cut, cancellationToken).ConfigureAwait(false);
        await VerifySurvivorOutcomesAsync(app, seed, cut, cancellationToken).ConfigureAwait(false);
        return cut with { RequiredApplied = requiredApplied };
    }

    internal static async Task VerifyLeaderRejoinAsync(DistributedApplication app, RequestCqrsRf3Wave wave,
        NodeEpochRf3Profile profile, DueFaultRf3Seed seed, DueFaultRf3LeaderCut cut,
        CancellationToken cancellationToken)
    {
        await wave.RestartAsync(cut.LeaderNode, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(app, profile, cut.RequiredApplied, cancellationToken)
            .ConfigureAwait(false);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(cut.BeforeStatus, status).ConfigureAwait(false);
        await AssertAppliedAsync(status, cut.RequiredApplied).ConfigureAwait(false);
        var current = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        var index = Array.FindIndex(cut.BeforeDiscovery, value => value.VoterId == VoterOrigin(cut.LeaderNode));
        if (index < 0)
        { throw new InvalidOperationException(LeaderMissing); }
        await RequestCqrsRf3DiscoveryOracle.AssertOneReplacementAsync(cut.BeforeDiscovery, current, index)
            .ConfigureAwait(false);
        await VerifySurvivorOutcomesAsync(app, seed, cut, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySurvivorProgressAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        DueFaultRf3Seed seed, DueFaultRf3LeaderCut cut, CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        using var firstHttp = McpCallerHttp.Create(app, cut.Survivors[0]);
        using var secondHttp = McpCallerHttp.Create(app, cut.Survivors[1]);
        var firstSdk = new KeyLoadClient(firstHttp, profile.AdminKey, IntegrationClientOptions.Execution());
        var secondSdk = new KeyLoadClient(secondHttp, profile.AdminKey, IntegrationClientOptions.Execution());
        var observed = await WaitForReplacementLeaderAsync(firstSdk, secondSdk, cut, deadline.Token).ConfigureAwait(false);
        await using var firstAdmin = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[0], cut.Survivors[0],
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await using var secondAdmin = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[1], cut.Survivors[1],
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await AssertSnapshotMatchesAsync(firstAdmin, cut.Survivors[0], observed.First, deadline.Token).ConfigureAwait(false);
        await AssertSnapshotMatchesAsync(secondAdmin, cut.Survivors[1], observed.Second, deadline.Token).ConfigureAwait(false);
        await AssertSurvivorDiscoveryAsync(app, profile, cut, cancellationToken).ConfigureAwait(false);
        await using var firstData = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[0], cut.Survivors[0],
            seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
        await using var secondData = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[1], cut.Survivors[1],
            seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
        await WaitUntilDueAsync(seed.DueAt, cancellationToken).ConfigureAwait(false);
        await WaitForDueEffectsAsync(firstData.Sdk, secondData.Sdk, seed, cancellationToken).ConfigureAwait(false);
        await Assert.That(observed.First.Leader).IsNotEqualTo(VoterOrigin(cut.LeaderNode));
    }

    private static async Task<(NodeStatus First, NodeStatus Second)> WaitForReplacementLeaderAsync(
        KeyLoadClient first, KeyLoadClient second, DueFaultRf3LeaderCut cut, CancellationToken cancellationToken)
    {
        while (true)
        {
            var a = await first.StatusAsync(cancellationToken).ConfigureAwait(false);
            var b = await second.StatusAsync(cancellationToken).ConfigureAwait(false);
            if (HasReplacementLeader(a, b, cut, out _))
            { return (a.Value!, b.Value!); }
            await Task.Delay(TimeSpan.FromMilliseconds(DueFaultRf3Protocol.PollMilliseconds), TimeProvider.System,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasReplacementLeader(ManagedCode.Communication.Result<NodeStatus> first,
        ManagedCode.Communication.Result<NodeStatus> second, DueFaultRf3LeaderCut cut, out string leader)
    {
        leader = string.Empty;
        if (!first.IsSuccess || !second.IsSuccess || first.Value is not { RoutingReady: true, Voters: 3 } a
            || second.Value is not { RoutingReady: true, Voters: 3 } b || a.Leader is null
            || a.Leader != b.Leader || a.Leader == VoterOrigin(cut.LeaderNode))
        { return false; }
        if (!cut.Survivors.Any(name => VoterOrigin(name) == a.Leader))
        { return false; }
        leader = a.Leader;
        return true;
    }

    private static async Task AssertSnapshotMatchesAsync(NodeEpochRf3Callers callers, string nodeName,
        NodeStatus status, CancellationToken cancellationToken)
    {
        var reply = await callers.Mcp.Client.InvokeKeyLoadToolAsync(AdminDashboardProtocol.SnapshotTool,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var snapshot = await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(reply).ConfigureAwait(false);
        await Assert.That(snapshot.Value.Node.NodeId).IsEqualTo(status.NodeId);
        await Assert.That(snapshot.Value.Node.Incarnation).IsEqualTo(status.Incarnation);
        await Assert.That(snapshot.Value.Node.Leader).IsEqualTo(status.Leader);
        await Assert.That(snapshot.Value.Node.RoutingReady).IsTrue();
        await Assert.That(snapshot.Value.LocalVoter).IsEqualTo(VoterOrigin(nodeName));
    }

    private static async Task AssertSurvivorDiscoveryAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DueFaultRf3LeaderCut cut, CancellationToken cancellationToken)
    {
        foreach (var node in cut.BeforeStatus.Where(value => value.Name != cut.LeaderNode))
        {
            var current = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, node.Name, profile,
                cancellationToken).ConfigureAwait(false);
            await RequestCqrsRf3DiscoveryOracle.AssertCurrentAsync(current).ConfigureAwait(false);
            await Assert.That(current.VoterId).IsEqualTo(node.Admin.LocalVoter);
            await Assert.That(current.SiloAddress).IsEqualTo(OriginalAddress(cut.BeforeDiscovery, current.VoterId));
        }
    }

    private static async Task WaitForDueEffectsAsync(KeyLoadClient first, KeyLoadClient second,
        DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        await WaitUntilDueAsync(seed.DueAt, deadline.Token).ConfigureAwait(false);
        while (!await DueFaultRf3Assertions.AreBothTransitionsCommittedAsync(first, second, seed, deadline.Token)
            .ConfigureAwait(false))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(DueFaultRf3Protocol.PollMilliseconds), TimeProvider.System, deadline.Token)
                .ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilDueAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var delay = dueAt - TimeProvider.System.GetUtcNow();
        if (delay > TimeSpan.Zero)
        { await Task.Delay(delay, TimeProvider.System, cancellationToken).ConfigureAwait(false); }
    }

    private static async Task<long> ReadSurvivorHighWaterAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DueFaultRf3LeaderCut cut, CancellationToken cancellationToken)
    {
        await using var first = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[0], cut.Survivors[0],
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await using var second = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[1], cut.Survivors[1],
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        var a = await McpCallerAssertions.SdkSuccessAsync(await first.Sdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var b = await McpCallerAssertions.SdkSuccessAsync(await second.Sdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        return Math.Max(a.Applied, b.Applied);
    }

    private static async Task VerifySurvivorOutcomesAsync(Aspire.Hosting.DistributedApplication app,
        DueFaultRf3Seed seed, DueFaultRf3LeaderCut cut,
        CancellationToken cancellationToken)
    {
        await using var first = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[0], cut.Survivors[0],
            seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
        await using var second = await NodeEpochRf3Callers.ConnectAsync(app, cut.Survivors[1], cut.Survivors[1],
            seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
        await DueFaultRf3Assertions.AssertOutcomesAsync(first, second, seed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AssertAppliedAsync(IReadOnlyList<NodeEpochRf3NodeObservation> nodes, long required)
    {
        foreach (var node in nodes)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(required); }
    }

    private static NodeEpochRf3NodeObservation FindLeader(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        var leaders = nodes.Select(value => value.Status.Leader).Where(value => value is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1)
        { throw new InvalidOperationException(LeaderMissing); }
        if (nodes.Any(node => node.Status.Leader != leaders[0]))
        { throw new InvalidOperationException(LeaderMissing); }
        return nodes.Single(value => value.Admin.LocalVoter == leaders[0]);
    }

    private static string OriginalAddress(IReadOnlyList<ReplicaSiloDiscovery> values, string? voter)
        => values.Single(value => value.VoterId == voter).SiloAddress;

    private static string VoterOrigin(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => "http://node1:8080",
        RequestCqrsRf3Protocol.Node2 => "http://node2:8080",
        RequestCqrsRf3Protocol.Node3 => "http://node3:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };

}
