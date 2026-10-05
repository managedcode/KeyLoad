using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using T = KeyLoad.UnitTests.Features.BenchmarkComparisons.NativeQuorumTopologyTokens;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003: actual native request configuration uses the selected member count and majority.</summary>
internal sealed class NativeQuorumTopologyConfigurationTests
{
    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 2)]
    [Arguments(3, 2)]
    public async Task AcIso003NativeConfigurationUsesExactCountAndQuorum(int nodes, int quorum)
    {
        var topology = ComparisonTopologies.FromNodeCount(nodes);
        using var collection = JsonDocument.Parse(JsonSerializer.Serialize(QdrantNativePolicy.CreateCollection(128, topology)));
        await Assert.That(collection.RootElement.GetProperty(T.Replication).GetInt32()).IsEqualTo(nodes);
        await Assert.That(collection.RootElement.GetProperty(T.Consistency).GetInt32()).IsEqualTo(quorum);
        await Assert.That(collection.RootElement.GetProperty(T.ShardNumber).GetInt32()).IsEqualTo(1);
        var queue = RabbitNativePolicy.QueueArguments(topology);
        await Assert.That(queue[T.QueueType]).IsEqualTo(T.Quorum);
        await Assert.That(queue[T.GroupSize]).IsEqualTo(nodes);
        await Assert.That(QdrantNativePolicy.SeedSuffix(topology)).IsEqualTo(nodes > 1 ? T.SeedSuffix : string.Empty);
        await Assert.That(QdrantNativePolicy.QuerySuffix(topology)).IsEqualTo(nodes > 1 ? T.QuerySuffix : string.Empty);
    }

    [Test]
    public async Task AcIso003TwoNodeLabelsRequireBothMembersAndNeverClaimFailureAvailability()
    {
        await Assert.That(QdrantNativePolicy.TopologyLabel(ComparisonTopology.TwoNode)).Contains("quorum 2 of 2");
        await Assert.That(RabbitNativePolicy.TopologyLabel(ComparisonTopology.TwoNode)).Contains("quorum 2 of 2");
        await Assert.That(QdrantNativePolicy.TopologyLabel(ComparisonTopology.TwoNode)).Contains("no single-node-loss availability");
        await Assert.That(RabbitNativePolicy.TopologyLabel(ComparisonTopology.TwoNode)).Contains("no single-node-loss availability");
    }

    [Test]
    [Arguments(-1)]
    [Arguments(3)]
    public async Task AcIso003UnknownTopologyFailsBeforeClientsMakeRequests(int value)
    {
        using var http = new HttpClient();
        var topology = (ComparisonTopology)value;
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var target = new QdrantTarget(http, T.RunId, T.Image, UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Lifecycle(), topology);
        });
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var target = new RabbitTarget(T.BrokerConnection, T.RunId, T.Image, UnitBenchmarkOptions.Lifecycle(), topology);
        });
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public void AcIso003EndpointConfigurationAcceptsExactlyTheSelectedDistinctCount(int nodes)
    {
        using var one = new HttpClient { BaseAddress = new Uri(T.EndpointOne) };
        using var two = new HttpClient { BaseAddress = new Uri(T.EndpointTwo) };
        using var three = new HttpClient { BaseAddress = new Uri(T.EndpointThree) };
        QdrantReplicaProof.ValidateClients(new[] { one, two, three }.Take(nodes).ToArray(),
            ComparisonTopologies.FromNodeCount(nodes));
    }

    [Test]
    [Arguments("missing")]
    [Arguments("same-client")]
    [Arguments("same-endpoint")]
    [Arguments("missing-address")]
    public async Task AcIso003TwoNodeEndpointMismatchFailsBeforeCollectionCreation(string corruption)
    {
        using var one = new HttpClient { BaseAddress = new Uri(T.EndpointOne) };
        using var two = new HttpClient { BaseAddress = new Uri(T.EndpointTwo) };
        var clients = corruption switch
        {
            "missing" => new[] { one },
            "same-client" => [one, one],
            "same-endpoint" => [one, two],
            "missing-address" => [one, two],
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        if (corruption == "same-endpoint")
        {
            two.BaseAddress = one.BaseAddress;
        }
        if (corruption == "missing-address")
        {
            two.BaseAddress = null;
        }
        var error = Assert.ThrowsExactly<ComparisonFailureException>(() =>
            QdrantReplicaProof.ValidateClients(clients, ComparisonTopology.TwoNode));
        await Assert.That(error!.Message).IsEqualTo(T.InvalidClients);
    }
}
