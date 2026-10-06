using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveTopologyTests
{
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private const string PartitionKey = "partition";

    [Test]
    public async Task AcTsi003OneTwoAndThreeNodeStatusInputsPreserveQuorumContract()
    {
        foreach (var count in new[] { 1, 2, 3 })
        {
            var voters = Voters(count);
            var statuses = Statuses(voters, voters[0]).Select((status, index) =>
                status with { NodeId = "node-" + (index + 1) }).ToImmutableArray();
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(statuses, voters, Incarnation);
            await Assert.That(statuses.Length).IsEqualTo(count);
            await Assert.That(statuses[0].Voters).IsEqualTo(count);
        }
    }

    [Test]
    public async Task AcTsi003DuplicateDriftMixedLeaderReadinessAndDurabilityFail()
    {
        var voters = Voters(3);
        var valid = Statuses(voters, voters[0]);
        var invalid = new[]
        {
            valid.SetItem(2, valid[2] with { NodeId = valid[1].NodeId }),
            valid.SetItem(2, valid[2] with { Voters = 2 }),
            valid.SetItem(2, valid[2] with { Incarnation = Guid.NewGuid() }),
            valid.SetItem(2, valid[2] with { Leader = voters[2] }),
            valid.SetItem(2, valid[2] with { Leader = "outside" }),
            valid.SetItem(2, valid[2] with { RoutingReady = false }),
            valid.SetItem(2, valid[2] with { Durability = DurabilityProfile.ProcessDurable })
        };

        foreach (var candidate in invalid)
        {
            Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
                KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(candidate, voters, Incarnation));
        }

        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(valid,
                [voters[0], voters[0], voters[2]], Incarnation));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(valid[..^1], voters, Incarnation));

        await Assert.That(valid.Select(status => status.NodeId).Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task AcTsi003DashboardMustProveExpectedLocalVoterAndOrderedMembership()
    {
        var voters = Voters(2);
        var status = Statuses(voters, voters[0])[0];
        var snapshot = Snapshot(status, voters[0], voters);
        KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(snapshot, voters[0], voters, Incarnation);
        await Assert.That(snapshot.LocalVoter).IsEqualTo(voters[0]);

        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(
                Snapshot(status, voters[1], voters), voters[0], voters, Incarnation));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(
                Snapshot(status, voters[0], [voters[1], voters[0]]), voters[0], voters, Incarnation));
    }

    [Test]
    public async Task AcTsi003ContextRejectsDuplicatePeerIdentityEndpointAndClient()
    {
        using var firstHttp = new HttpClient();
        using var secondHttp = new HttpClient();
        var firstClient = new KeyLoadClient(firstHttp, string.Empty, UnitClientOptions.Execution());
        var secondClient = new KeyLoadClient(secondHttp, string.Empty, UnitClientOptions.Execution());
        var first = new KeyLoadTimeSeriesIntensivePeer(1, "voter-1", new("http://127.0.0.1:7101"), firstClient);
        var second = new KeyLoadTimeSeriesIntensivePeer(2, "voter-2", new("http://127.0.0.1:7102"), secondClient);
        var partition = new PartitionRef("tenant", "database", "domain", PartitionKey);
        var valid = new KeyLoadTimeSeriesIntensiveContext("run", partition, "metrics", Incarnation, [first, second]);
        await Assert.That(valid.Peers.Length).IsEqualTo(2);
        await Assert.That(valid.ExpectedAtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);

        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Index = 1 }]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { VoterId = "voter-1" }]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Endpoint = first.Endpoint }]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Client = firstClient }]));
    }

    private static ImmutableArray<string> Voters(int count) =>
        Enumerable.Range(1, count).Select(index => "voter-" + index).ToImmutableArray();

    private static ImmutableArray<NodeStatus> Statuses(ImmutableArray<string> voters, string leader) => voters
        .Select(voter => new NodeStatus(voter, Incarnation, 20, leader, voters.Length,
            DurabilityProfile.QuorumProcessDurable, true, 123))
        .ToImmutableArray();

    private static AdminNodeSnapshot Snapshot(NodeStatus status, string localVoter,
        ImmutableArray<string> voters) => new(DateTimeOffset.UnixEpoch, status, null!, null!, null!)
        { LocalVoter = localVoter, Voters = voters };
}
