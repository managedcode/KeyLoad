using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using KeyLoad.UnitTests;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class KeyLoadTimeSeriesIntensiveTopologyTests
{
    private static readonly Guid Incarnation = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private const string PartitionKey = "partition";

    [Test]
    public async Task AcTsi003OneAndThreeSiloInputsPreserveNativeMembershipObservations()
    {
        foreach (var count in new[] { 1, 3 })
        {
            var silos = Silos(count);
            var statuses = Statuses(silos).Select((status, index) =>
                status with
                {
                    NodeId = "node-" + (index + 1)
                }).ToImmutableArray();
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(statuses, silos, Incarnation);
            await Assert.That(statuses.Length).IsEqualTo(count);
            await Assert.That(statuses[0].SiloCount).IsEqualTo(count);
        }
    }

    [Test]
    public async Task AcTsi003DuplicateIdentityCountDriftReadinessAndDurabilityFail()
    {
        var silos = Silos(3);
        var valid = Statuses(silos);
        var invalid = new[]
        {
            valid.SetItem(2, valid[2] with { NodeId = valid[1].NodeId }),
            valid.SetItem(2, valid[2] with { SiloCount = 2 }),
            valid.SetItem(2, valid[2] with { Incarnation = Guid.NewGuid() }),
            valid.SetItem(2, valid[2] with { ReplicaCount = 2 }),
            valid.SetItem(2, valid[2] with { RoutingReady = false }),
            valid.SetItem(2, valid[2] with { Durability = DurabilityProfile.Buffered })
        };

        foreach (var candidate in invalid)
        {
            Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
                KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(candidate, silos, Incarnation));
        }

        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(valid,
                [silos[0], silos[0], silos[2]], Incarnation));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateStatuses(valid[..^1], silos, Incarnation));

        await Assert.That(valid.Select(status => status.NodeId).Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task AcTsi003DashboardMustReportTheExpectedNativeSiloAndMembership()
    {
        var silos = Silos(3);
        var status = Statuses(silos)[0];
        var snapshot = Snapshot(status, silos[0], silos);
        KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(snapshot, silos[0], silos, Incarnation);
        await Assert.That(snapshot.SiloName).IsEqualTo(silos[0]);

        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(
                Snapshot(status, silos[1], silos), silos[0], silos, Incarnation));
        Assert.ThrowsExactly<KeyLoadTimeSeriesIntensiveReplyException>(() =>
            KeyLoadTimeSeriesIntensiveTopology.ValidateDashboard(
                Snapshot(status, silos[0], [silos[1], silos[0], silos[2]]), silos[0], silos, Incarnation));
    }

    [Test]
    public async Task AcTsi003ContextRejectsDuplicatePeerIdentityEndpointAndClient()
    {
        using var firstHttp = new HttpClient();
        using var secondHttp = new HttpClient();
        using var thirdHttp = new HttpClient();
        var firstClient = new KeyLoadClient(firstHttp, string.Empty, UnitClientOptions.Execution());
        var secondClient = new KeyLoadClient(secondHttp, string.Empty, UnitClientOptions.Execution());
        var thirdClient = new KeyLoadClient(thirdHttp, string.Empty, UnitClientOptions.Execution());
        var first = new KeyLoadTimeSeriesIntensivePeer(1, "node1", new("http://127.0.0.1:7101"), firstClient);
        var second = new KeyLoadTimeSeriesIntensivePeer(2, "node2", new("http://127.0.0.1:7102"), secondClient);
        var third = new KeyLoadTimeSeriesIntensivePeer(3, "node3", new("http://127.0.0.1:7103"), thirdClient);
        var partition = new PartitionRef("tenant", "database", "domain", PartitionKey);
        var valid = new KeyLoadTimeSeriesIntensiveContext("run", partition, "metrics", Incarnation, [first, second, third]);
        await Assert.That(valid.Peers.Length).IsEqualTo(3);
        await Assert.That(valid.ExpectedAtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);

        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Index = 1 }, third]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { SiloName = "node1" }, third]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Endpoint = first.Endpoint }, third]));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new KeyLoadTimeSeriesIntensiveContext(
                "run", partition, "metrics", Incarnation, [first, second with { Client = firstClient }, third]));
    }

    private static ImmutableArray<string> Silos(int count) =>
        Enumerable.Range(1, count).Select(index => "node" + index).ToImmutableArray();

    private static ImmutableArray<NodeStatus> Statuses(ImmutableArray<string> silos) => silos
        .Select(silo => new NodeStatus(silo, Incarnation, 20, silos.Length, silos.Length,
            DurabilityProfile.ProcessDurable, true, 123, 1))
        .ToImmutableArray();

    private static AdminNodeSnapshot Snapshot(NodeStatus status, string localSilo,
        ImmutableArray<string> silos) => new(DateTimeOffset.UnixEpoch, status, null!, null!, null!)
        {
            SiloName = localSilo,
            ActiveSilos = silos
        };
}
