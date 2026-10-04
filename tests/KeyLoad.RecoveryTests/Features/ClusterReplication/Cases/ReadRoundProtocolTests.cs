using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-006: genuine stored protocol integration, not native Orleans, fault or performance qualification.</summary>
internal sealed class ReadRoundProtocolTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    /// <summary>Actual elected nodes distinguish local/remote application and native control rounds at the same committed cut.</summary>
    /// <param name="count">Actual independently stored voter count.</param>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ActualStoredQuorumRetainsCommittedDataAcrossBothReadPurposes(int count)
    {
        await using var cluster = new ReadRoundStoredCluster(count);
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        cluster.Attach();
        var leader = await cluster.ReadyLeaderAsync(linked.Token);
        await leader.Consensus.SubmitAsync(leader.Database.NormalizeOperation(ReplicaCrashModel.Operation(1)), linked.Token);
        var committed = await leader.Consensus.SubmitAsync(leader.Database.NormalizeOperation(ReplicaCrashModel.Operation(2)), linked.Token);
        var receipt = committed.Get<CommitReceipt>();
        await leader.Consensus.ReadBarrierAsync(linked.Token);
        await leader.Consensus.ReadControlBarrierAsync(linked.Token);
        foreach (var node in cluster.Nodes)
        {
            await node.Consensus.ReadBarrierAsync(linked.Token);
            await node.Consensus.ReadControlBarrierAsync(linked.Token);
            await AssertCommittedAsync(node, receipt, linked.Token);
            if (node != leader)
            {
                await Assert.That(node.Transport.Count(ReplicaRpc.ReadBarrier)).IsGreaterThan(0);
                await Assert.That(node.Transport.Count(ReplicaRpc.ControlReadBarrier)).IsGreaterThan(0);
            }
        }
        await Assert.That(leader.Transport.Count(ReplicaRpc.ReadProbe) > 0).IsEqualTo(count > 1);
        var replayed = (await leader.Consensus.SubmitAsync(leader.Database.NormalizeOperation(ReplicaCrashModel.Operation(2)), linked.Token)).Get<CommitReceipt>();
        await Assert.That(replayed.CommandId).IsEqualTo(receipt.CommandId);
        await Assert.That(replayed.Token).IsEqualTo(receipt.Token);
        await Assert.That(replayed.Mutations).IsEquivalentTo(receipt.Mutations);
    }

    /// <summary>A native control round never emits an application read probe in a real two-voter group.</summary>
    [Test]
    public async Task RemoteControlRoundDoesNotSpendApplicationProbeTraffic()
    {
        await using var cluster = new ReadRoundStoredCluster(2);
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        cluster.Attach();
        var leader = await cluster.ReadyLeaderAsync(linked.Token);
        var follower = cluster.Nodes.Single(node => node != leader);
        await follower.Consensus.ReadBarrierAsync(linked.Token);
        var probes = leader.Transport.Count(ReplicaRpc.ReadProbe);
        await Assert.That(probes).IsGreaterThan(0);
        await follower.Consensus.ReadControlBarrierAsync(linked.Token);
        await Assert.That(leader.Transport.Count(ReplicaRpc.ReadProbe)).IsEqualTo(probes);
        await Assert.That(follower.Transport.Count(ReplicaRpc.ControlReadBarrier)).IsEqualTo(1);
    }

    private static async Task AssertCommittedAsync(ReadRoundStoredNode node, CommitReceipt receipt, CancellationToken token)
    {
        var state = await node.Consensus.StateAsync(token);
        await Assert.That(state.MaterializedPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await Assert.That(state.CommittedIndex).IsGreaterThanOrEqualTo(receipt.Token.Position);
        var found = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, ReplicaCrashModel.Document);
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Revision).IsEqualTo(1);
        await Assert.That(found.Json).IsEqualTo(ReplicaCrashModel.JsonAt(2));
        await Assert.That(node.Database.Outcome(ReplicaCrashModel.PrincipalId, receipt.CommandId)!.Get<CommitReceipt>().Token)
            .IsEqualTo(receipt.Token);
    }
}
