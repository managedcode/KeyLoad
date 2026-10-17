using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUnverifiedProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUnverifiedAssertions
{
    internal static async Task TerminalAsync(ReadExecutionBudget work, Exception original,
        ReplicaForwardMetadataSample sample, ReplicaForwardUnverifiedObservation observation,
        Guid id, DateTimeOffset expiry, CancellationToken token)
    {
        var settled = await sample.Lease.Settlement.WaitAsync(token);
        await Assert.That(settled.OriginalFailure).IsNull();
        await Assert.That(settled.CleanupOrJoinFailure).IsNull();
        await Assert.That(sample.Work.Budget.ReadBytes).IsGreaterThan(P.NoBytes);
        await Assert.That(sample.Work.Budget.ConstrainedNativeRecords).IsGreaterThan(P.NoRecords);
        await Assert.That(sample.ReceiverExpiry).IsEqualTo(expiry);
        await Assert.That(work.ReadBytes).IsEqualTo(sample.SourceBeforeBytes);
        await Assert.That(work.ConstrainedNativeRecords).IsEqualTo(sample.SourceBeforeRecords);
        await Assert.That(work.HasIndeterminateMetadata).IsTrue();
        await Assert.That(work.RemainingReadGrantBytes).IsEqualTo(P.NoBytes);
        await Assert.That(work.RemainingReadGrantRecords).IsEqualTo(P.NoRecords);
        await Assert.That(observation.Count(id)).IsEqualTo(P.One);
        var failure = Assert.ThrowsExactly<KeyLoadException>(work.Check);
        await Assert.That(ReferenceEquals(failure, original)).IsTrue();
    }

    internal static async Task StateAsync(RequestCqrsCohortNativeNode[] nodes, ReplicatedOperation original,
        ResourceLifetimeV1 lifetime, CommitReceipt expected, string documentId, string json,
        long expectedOutboxRecords, CancellationToken token)
    {
        var partition = NativeCommandPayload.Read<CommandRequest>(
            nodes[0].Journal.Database.VerifyOperationAuthority(original)).Partition;
        var reference = new EntityRef(partition, P.Collection, documentId)
        {
            Lifetime = new EntityRefLifetimeV1(lifetime.Incarnation.Value)
        };
        foreach (var node in nodes)
        {
            var result = OutcomeStoreOracle.ReadPartition(node.Journal.Database.Store, reference.Partition, P.Writer, original.Id)
                ?? throw new InvalidOperationException(P.Missing);
            await Assert.That(result.Error).IsNull();
            await Assert.That(result.SafeDetail).IsNull();
            await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, result.Get<CommitReceipt>());
            var outbox = node.Journal.Database.GetOutboxStatus(ReplicaForwardedOriginalWorkProtocol.Root, partition);
            await Assert.That(outbox.Head.Tail).IsEqualTo(expectedOutboxRecords);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(expectedOutboxRecords);
            var entry = node.Journal.Log.ReadEntry(expected.Token.Position);
            await Assert.That(entry?.Operation?.Id).IsEqualTo(original.Id);
            await Assert.That(entry!.Operation!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
            await ReplicaForwardedOriginalWorkAssertions.EqualAsync<DocumentResult?>(
                new(reference, P.FirstRevision, json, false, []),
                node.Journal.Database.GetDocument(ReplicaForwardedOriginalWorkProtocol.Root,
                    new(reference.Partition, P.Collection, documentId), cancellationToken: token));
        }
    }
}
