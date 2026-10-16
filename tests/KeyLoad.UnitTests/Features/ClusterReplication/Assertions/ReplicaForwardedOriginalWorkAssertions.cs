using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardedOriginalWorkProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardedOriginalWorkAssertions
{
    internal static async Task SameOriginalAsync(ReplicaForwardedOriginalWorkObserved observed)
    {
        await Assert.That(observed.CreatedReadBytes).IsEqualTo(P.NoReadBytes);
        await Assert.That(observed.BeforeExecuteReadBytes > P.NoReadBytes).IsTrue();
        await Assert.That(observed.SameBudget && observed.SameBinding && observed.SameLedger).IsTrue();
    }

    internal static async Task JoinedAsync(ReplicaForwardedOriginalWorkObserved observed, CancellationToken token)
    {
        var original = observed.Lease.Settlement;
        var settled = await original.WaitAsync(token);
        await Assert.That(ReferenceEquals(original, observed.Lease.Settlement)).IsTrue();
        await Assert.That(settled.CleanupOrJoinFailure).IsNull();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Task.FromResult(observed.Ledger.RetainedEntries));
    }

    internal static async Task ReceiptAsync(RequestCqrsCohortNativeNode leader, PartitionRef partition,
        Guid commandId, string key, long index, CommitReceipt actual)
    {
        var expected = new CommitReceipt(commandId,
            new(leader.Journal.Database.Store.Identity.Incarnation, partition.AtomicPartitionId, index, P.InitialRevision),
            [new(P.PutKind, P.Collection, key, P.InitialRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var entry = leader.Journal.Log.ReadEntry(index);
        await Assert.That(entry?.Operation?.Id).IsEqualTo(commandId);
        await Assert.That(entry?.Operation?.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(entry?.Operation?.PrincipalId).IsEqualTo(P.Root);
    }

    internal static async Task DocumentsAsync(RequestCqrsCohortNativeNode[] nodes, PartitionRef partition,
        CancellationToken token)
    {
        foreach (var node in nodes)
        {
            var database = node.Journal.Database;
            await EqualAsync<DocumentResult?>(new DocumentResult(new(partition, P.Collection, P.Anchor), P.InitialRevision, P.AnchorJson, false, []),
                database.GetDocument(P.Root, new(partition, P.Collection, P.Anchor), cancellationToken: token));
            await EqualAsync<DocumentResult?>(new DocumentResult(new(partition, P.Collection, P.Healthy), P.InitialRevision, P.HealthyJson, false, []),
                database.GetDocument(P.Root, new(partition, P.Collection, P.Healthy), cancellationToken: token));
            await Assert.That(database.GetDocument(P.Root, new(partition, P.Collection, P.Cancelled), cancellationToken: token)).IsNull();
        }
    }

    internal static async Task DependencyAsync(ReplicaForwardedOriginalWorkObserved observed,
        RequestCqrsCohortNativeNode leader, PartitionRef partition, ResourceDefinition definition)
    {
        var lifetime = leader.Journal.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            partition.TenantId, partition.DatabaseId, P.Collection, definition));
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(observed.Lease.Dependencies.Count).IsEqualTo(P.SingleDependency);
        await Assert.That((ResourceLifetimeV1?)observed.Lease.Dependencies.Single()).IsEqualTo(lifetime);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
