using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Protocol;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationFlowProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Checks literal complete operations and explicit native authority rather than health-only readiness.</summary>
internal static class ReplicaIsolationFlowAssertions
{
    internal static async Task ReceiptAsync(CommitReceipt receipt, CommandRequest command, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Position > ReplicaIsolationFlowProtocol.Zero).IsTrue();
        await Assert.That(receipt.Token.Incarnation != Guid.Empty).IsTrue();
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(ReplicaIsolationFlowProtocol.First);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations.Single()).SequenceEqual(
            NativeSerialization.Serialize(new MutationReceipt(ReplicaIsolationFlowProtocol.Mutation, Collection, DocumentId, revision)))).IsTrue();
    }

    internal static async Task StatusAsync(NodeStatus status, string node, string leader, Guid incarnation, long minimumTerm)
    {
        await Assert.That(Guid.TryParse(status.NodeId, out var identity) && identity != Guid.Empty).IsTrue();
        await Assert.That(status.NodeId).IsEqualTo(node);
        await Assert.That(status.Incarnation).IsEqualTo(incarnation);
        await Assert.That(status.Voters).IsEqualTo(Voters);
        await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.ProcessId > ReplicaIsolationFlowProtocol.Zero).IsTrue();
        await Assert.That(status.ReadGeneration > ReplicaIsolationFlowProtocol.Zero).IsTrue();
        await Assert.That(new Uri(status.Leader!).Host).IsEqualTo(leader);
        await Assert.That(status.ConsensusTerm >= minimumTerm).IsTrue();
    }

    internal static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        CommitToken minimum, string secret, CancellationToken cancellationToken)
    {
        var failed = await sdk.GetAsync(reference, minimum, cancellationToken);
        await Assert.That(failed.IsSuccess).IsFalse();
        await Assert.That(failed.Value).IsNull();
        await DocumentSessionReadRf3NoQuorum.VerifyAsync(sdk, mcp, reference, minimum, secret, cancellationToken);
    }

    internal static async Task ReplayedAsync(KeyLoadClient client, CommandRequest command, CommitReceipt receipt,
        CancellationToken cancellationToken)
    {
        var replay = await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, cancellationToken));
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
    }
}
