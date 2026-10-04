using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3CurrentSnapshotAssertions
{
    internal static async Task VerifyAllAsync(string priorRoot, string currentRoot,
        NodeEpochRf3Profile profile, NodeEpochRf3Migration migration, CancellationToken cancellationToken)
    {
        foreach (var nodeName in NodeNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(priorRoot, nodeName);
            var target = Path.Combine(currentRoot, nodeName);
            var prior = migration.PriorSnapshot(nodeName);
            var current = NodeEpochRf3ReceiptReader.VerifyCurrentSnapshot(source, target, profile, nodeName);
            await VerifyPointerAsync(prior, current, profile).ConfigureAwait(false);
        }
    }

    private static async Task VerifyPointerAsync(ReplicaSnapshot prior, ReplicaSnapshot current,
        NodeEpochRf3Profile profile)
    {
        await Assert.That(current.Incarnation).IsEqualTo(profile.Incarnation);
        await Assert.That(current.Index).IsGreaterThanOrEqualTo(prior.Index + NodeEpochRf3Protocol.SnapshotThreshold);
        await Assert.That(current.Term).IsGreaterThanOrEqualTo(prior.Term);
        await Assert.That(current.Length).IsGreaterThan(0L);
        await Assert.That(current.TransferId).IsNotEqualTo(prior.TransferId);
        await Assert.That(current.Sha256.Length).IsEqualTo(64);
        await Assert.That(current.FileName.Length).IsGreaterThan(0);
    }

    private static string[] NodeNames() =>
    [
        NodeEpochRf3Protocol.Node1,
        NodeEpochRf3Protocol.Node2,
        NodeEpochRf3Protocol.Node3
    ];
}
