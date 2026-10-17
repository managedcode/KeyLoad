using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaSnapshotReadinessStateTests
{
    /// <summary>AC-EMPTY-SNAPSHOT-READINESS-001: metadata reports the actual native prefix before and after joined reclamation.</summary>
    [Test]
    public async Task PublishedCheckpointPrefixRemainsVisibleUntilNativeReclamationJoins()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var fixture = new ReplicaCheckpointProtocolGateFixture();
        await fixture.RunAsync(async () =>
        {
            await fixture.PrepareAsync(receive: false, token);
            var node = fixture.Node;
            var consensus = new ReplicaConsensus(node.Materializer,
                RecoveryExecutionOptions.Configuration(node.Configuration), RecoveryExecutionOptions.Replica());
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var published = node.Snapshots.Create(ReplicaCheckpointProtocolGateNode.SnapshotCut,
                    ReplicaCheckpointProtocolGateNode.Term);
                var ordinary = await consensus.StateAsync(token);
                await Assert.That(ordinary.HasCheckpointPrefix).IsNull();
                await Assert.That(ordinary.CheckpointPending).IsNull();
                var before = await consensus.ReadCheckpointStateAsync(token);
                await Assert.That(before.SnapshotIndex).IsEqualTo(published.Index);
                await Assert.That(before.HasCheckpointPrefix is true).IsTrue();
                await Assert.That(before.CheckpointPending is false).IsTrue();
                var reclaimed = await node.Materializer.CreateCheckpointAsync(token);
                await Assert.That(reclaimed).IsEqualTo(published);
                var after = await consensus.ReadCheckpointStateAsync(token);
                await Assert.That(after.HasCheckpointPrefix is false).IsTrue();
                await Assert.That(after.CheckpointPending is false).IsTrue();
                await Assert.That(after.SnapshotIndex).IsEqualTo(before.SnapshotIndex);
                await Assert.That(after.LastIndex).IsEqualTo(ReplicaCheckpointProtocolGateNode.TailIndex);
                await fixture.AssertPublishedAsync(published, token);
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => consensus.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        });
    }
}
