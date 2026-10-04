using System.Text.Json.Nodes;
using T = KeyLoad.UnitTests.Features.BenchmarkComparisons.NativeQuorumTopologyTokens;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003: exact native running disc brokers must equal durable quorum members and online members.</summary>
internal sealed class NativeQuorumTopologyRabbitTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AcIso003ExactNativeQuorumQueueIsAccepted(int nodes)
    {
        var proof = NativeQuorumTopologyResponses.ReadRabbit(nodes);
        await Assert.That(proof.HasValue).IsTrue();
        await Assert.That(proof!.Value.Evidence.Nodes).IsEqualTo(nodes);
        await Assert.That(proof.Value.Evidence.DataCopies).IsEqualTo(nodes);
        await Assert.That(proof.Value.Version).IsEqualTo(T.BrokerVersion);
    }

    [Test]
    [Arguments("missing-node")]
    [Arguments("extra-offline-node")]
    [Arguments("duplicate-node")]
    [Arguments("offline-member")]
    [Arguments("ram-node")]
    [Arguments("empty-node")]
    [Arguments("missing-member")]
    [Arguments("extra-member")]
    [Arguments("foreign-member")]
    [Arguments("duplicate-member")]
    [Arguments("missing-online")]
    [Arguments("extra-online")]
    [Arguments("duplicate-online")]
    [Arguments("classic-queue")]
    [Arguments("transient-queue")]
    [Arguments("foreign-queue")]
    public async Task AcIso003TwoNodeRequiresExactNativeMembershipWithoutHiddenOfflineBrokers(string corruption)
    {
        var brokers = NativeQuorumTopologyResponses.Brokers(2);
        var queue = NativeQuorumTopologyResponses.Queue(2);
        Corrupt(brokers, queue, corruption);
        await Assert.That(NativeQuorumTopologyResponses.ReadRabbit(2, brokers, queue).HasValue).IsFalse();
    }

    [Test]
    public async Task AcIso003NativeVersionMustBeObserved()
        => await Assert.That(NativeQuorumTopologyResponses.ReadRabbit(2, version: string.Empty).HasValue).IsFalse();

    [Test]
    public async Task AcIso003NativeBrokerAndMemberOrderDoesNotChangeSetIdentity()
    {
        var brokers = NativeQuorumTopologyResponses.Brokers(2);
        var queue = NativeQuorumTopologyResponses.Queue(2);
        var reversed = new JsonArray(brokers.Reverse().Select(node => node!.DeepClone()).ToArray());
        queue[T.Online] = new JsonArray(queue[T.Online]!.AsArray().Reverse()
            .Select(node => node!.DeepClone()).ToArray());
        await Assert.That(NativeQuorumTopologyResponses.ReadRabbit(2, reversed, queue).HasValue).IsTrue();
    }

    private static void Corrupt(JsonArray brokers, JsonObject queue, string corruption)
    {
        var members = queue[T.Members]!.AsArray();
        var online = queue[T.Online]!.AsArray();
        Action mutation = corruption switch
        {
            "missing-node" => () => brokers.RemoveAt(1),
            "extra-offline-node" => () => brokers.Add(new JsonObject
            { [T.Name] = "rabbit@extra", [T.Running] = false, [T.Type] = T.Disc }),
            "duplicate-node" => () => brokers[1]![T.Name] = brokers[0]![T.Name]!.DeepClone(),
            "offline-member" => () => brokers[1]![T.Running] = false,
            "ram-node" => () => brokers[1]![T.Type] = T.Ram,
            "empty-node" => () => brokers[1]![T.Name] = string.Empty,
            "missing-member" => () => members.RemoveAt(1),
            "extra-member" => () => members.Add("rabbit@extra"),
            "foreign-member" => () => members[1] = "rabbit@extra",
            "duplicate-member" => () => members[1] = members[0]!.DeepClone(),
            "missing-online" => () => online.RemoveAt(1),
            "extra-online" => () => online.Add("rabbit@extra"),
            "duplicate-online" => () => online[1] = online[0]!.DeepClone(),
            "classic-queue" => () => queue[T.Type] = "classic",
            "transient-queue" => () => queue[T.Durable] = false,
            "foreign-queue" => () => queue[T.Name] = "unowned-queue",
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        mutation();
    }
}
