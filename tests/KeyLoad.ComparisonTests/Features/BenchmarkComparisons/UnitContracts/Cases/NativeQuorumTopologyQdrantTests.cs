using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using T = KeyLoad.UnitTests.Features.BenchmarkComparisons.NativeQuorumTopologyTokens;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003: native Qdrant proof requires exact identities, policy and a full active local shard.</summary>
internal sealed class NativeQuorumTopologyQdrantTests
{
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AcIso003EnabledNativeGroupsRequireEveryDistinctEndpoint(int nodes)
    {
        var proofs = NativeQuorumTopologyResponses.QdrantProofs(nodes);
        await Assert.That(QdrantNodeProof.Ready(proofs, ComparisonTopologies.FromNodeCount(nodes), nodes)).IsTrue();
        await Assert.That(proofs.Select(proof => proof.Peer).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodes);
        await Assert.That(proofs.All(proof => proof.Copies == 1 && proof.Shard == 0)).IsTrue();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("missing-node")]
    [Arguments("duplicate-node")]
    [Arguments("foreign-own-id")]
    [Arguments("empty-own-id")]
    [Arguments("different-version")]
    [Arguments("empty-version")]
    [Arguments("foreign-peer-set")]
    [Arguments("duplicate-peers")]
    [Arguments("extra-copy")]
    [Arguments("missing-copy")]
    [Arguments("different-shard")]
    public async Task AcIso003ThreeNodeCannotPassIncompleteOrInconsistentNativeProof(string corruption)
    {
        var proofs = NativeQuorumTopologyResponses.QdrantProofs(3);
        CorruptProof(proofs, corruption);
        await Assert.That(QdrantNodeProof.Ready(proofs, ComparisonTopology.Replicated, 3)).IsFalse();
    }

    [Test]
    [Arguments("missing-shard")]
    [Arguments("extra-shard")]
    [Arguments("inactive-shard")]
    [Arguments("partial-seed")]
    [Arguments("foreign-local-peer")]
    [Arguments("extra-shard-count")]
    public async Task AcIso003ThreeNodeRequiresExactlyOneActiveFullLocalSeededShard(string corruption)
    {
        var local = NativeQuorumTopologyResponses.Local(1);
        var result = local[T.Result]!;
        var shards = result[T.LocalShards]!.AsArray();
        Action mutation = corruption switch
        {
            "missing-shard" => shards.Clear,
            "extra-shard" => () => shards.Add(shards[0]!.DeepClone()),
            "inactive-shard" => () => shards[0]![T.State] = T.Partial,
            "partial-seed" => () => shards[0]![T.Points] = T.PointsCount - 1,
            "foreign-local-peer" => () => result[T.PeerId] = 99,
            "extra-shard-count" => () => result[T.ShardCount] = 2,
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        mutation();
        var proof = NativeQuorumTopologyResponses.ReadQdrant(3, 1, local: local);
        await Assert.That(proof.Copies).IsEqualTo(0);
    }

    [Test]
    [Arguments(1, T.Replication)]
    [Arguments(1, T.Consistency)]
    [Arguments(3, T.Replication)]
    [Arguments(3, T.Consistency)]
    public async Task AcIso003CollectionPolicyDriftIsAFailure(int nodes, string field)
    {
        var collection = NativeQuorumTopologyResponses.Collection(nodes);
        collection[T.Result]![T.Config]![T.Params]![field] = 99;
        var error = Assert.ThrowsExactly<ComparisonFailureException>(() =>
            NativeQuorumTopologyResponses.ReadQdrant(nodes, 1, collection: collection));
        await Assert.That(error!.Message).IsEqualTo(T.PolicyFailure);
    }

    [Test]
    public async Task AcIso003DisabledStandaloneRetainsGenuineNativeProofWithoutInventedPeers()
    {
        using var root = JsonDocument.Parse(new JsonObject { [T.Version] = T.NativeVersion }.ToJsonString());
        using var cluster = JsonDocument.Parse(new JsonObject
        { [T.Result] = new JsonObject { [T.Status] = T.Disabled } }.ToJsonString());
        var collection = NativeQuorumTopologyResponses.Collection(1);
        using var seeded = JsonDocument.Parse(collection.ToJsonString());
        var proof = QdrantNodeProof.ReadDisabled(root.RootElement, cluster.RootElement, seeded.RootElement, T.PointsCount);
        await Assert.That(proof).IsNotNull();
        var verified = proof!;
        await Assert.That(verified.Evidence.Nodes).IsEqualTo(1);
        await Assert.That(verified.Evidence.DataCopies).IsEqualTo(1);
        await Assert.That(verified.Evidence.Observations.All(item => !item.Contains("peer=", StringComparison.Ordinal))).IsTrue();
        collection[T.Result]![T.Points] = T.PointsCount - 1;
        using var partial = JsonDocument.Parse(collection.ToJsonString());
        await Assert.That(QdrantNodeProof.ReadDisabled(root.RootElement, cluster.RootElement,
            partial.RootElement, T.PointsCount)).IsNull();
    }

    [Test]
    [Arguments(3)]
    public async Task AcIso003DistributedGroupsRejectDisabledNativeCluster(int nodes)
    {
        var cluster = NativeQuorumTopologyResponses.Cluster(nodes, 1);
        cluster[T.Result]![T.Status] = T.Disabled;
        var error = Assert.ThrowsExactly<ComparisonFailureException>(() =>
            NativeQuorumTopologyResponses.ReadQdrant(nodes, 1, cluster: cluster));
        await Assert.That(error!.Message).IsEqualTo(T.NativeTopologyMismatch);
    }

    [Test]
    public async Task AcIso003NativePeerPropertyOrderDoesNotChangeSetIdentity()
    {
        var cluster = NativeQuorumTopologyResponses.Cluster(3, 1);
        var peers = cluster[T.Result]![T.Peers]!.AsObject();
        var reversed = peers.Reverse().Select(pair => (pair.Key, Value: pair.Value!.DeepClone())).ToArray();
        peers.Clear();
        foreach (var (key, value) in reversed)
        {
            peers.Add(key, value);
        }
        var proofs = NativeQuorumTopologyResponses.QdrantProofs(3);
        proofs[0] = NativeQuorumTopologyResponses.ReadQdrant(3, 1, cluster: cluster);
        await Assert.That(QdrantNodeProof.Ready(proofs, ComparisonTopology.Replicated, 3)).IsTrue();
    }

    private static void CorruptProof(List<(string Version, string Peer, string[] Peers, int Copies, int Shard)> proofs,
        string corruption)
    {
        var first = proofs[0];
        Action mutation = corruption switch
        {
            "empty" => proofs.Clear,
            "missing-node" => () => proofs.RemoveAt(1),
            "duplicate-node" => () => first.Peer = proofs[1].Peer,
            "foreign-own-id" => () => first.Peer = "99",
            "empty-own-id" => () => first.Peer = string.Empty,
            "different-version" => () => first.Version = "different",
            "empty-version" => () => first.Version = string.Empty,
            "foreign-peer-set" => () => first.Peers[1] = "99",
            "duplicate-peers" => () => first.Peers[1] = first.Peers[0],
            "extra-copy" => () => first.Copies = 2,
            "missing-copy" => () => first.Copies = 0,
            "different-shard" => () => first.Shard = 99,
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        mutation();
        if (proofs.Count != 0)
        {
            proofs[0] = first;
        }
    }
}
