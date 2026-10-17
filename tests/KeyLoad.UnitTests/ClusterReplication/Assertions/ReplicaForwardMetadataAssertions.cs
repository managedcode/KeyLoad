using KeyLoad.Core;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardMetadataProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardMetadataAssertions
{
    internal static async Task ImportedAsync(ReplicaForwardMetadataSample sample,
        ReadExecutionBudget original, CancellationToken token)
    {
        var settled = await sample.Lease.Settlement.WaitAsync(token);
        var receiver = sample.Work.Budget;
        await Assert.That(ReferenceEquals(sample.Source, original)).IsTrue();
        await Assert.That(settled.CleanupOrJoinFailure).IsNull();
        await Assert.That(receiver.ReadBytes > P.NoBytes).IsTrue();
        await Assert.That(receiver.ConstrainedNativeRecords > P.NoRecords).IsTrue();
        await Assert.That(original.ReadBytes - sample.SourceBeforeBytes).IsEqualTo(receiver.ReadBytes);
        await Assert.That(original.ConstrainedNativeRecords - sample.SourceBeforeRecords).IsEqualTo(receiver.ConstrainedNativeRecords);
        await Assert.That(sample.ReceiverMaximumReadBytes).IsEqualTo(original.MaximumNativeReadBytes - sample.SourceBeforeBytes);
        await Assert.That(sample.ReceiverMaximumRecords).IsEqualTo(original.MaximumNativeScanRecords - sample.SourceBeforeRecords);
        await Assert.That(sample.ReceiverExpiry).IsEqualTo(original.ExpiresAt);
        await Assert.That(original.HasIndeterminateMetadata).IsFalse();
        await UsableAsync(original);
    }

    internal static async Task UsableAsync(ReadExecutionBudget original)
    {
        await Assert.That(original.RemainingReadGrantBytes).IsEqualTo(original.MaximumNativeReadBytes - original.ReadBytes);
        await Assert.That(original.RemainingReadGrantRecords).IsEqualTo(original.MaximumNativeScanRecords - original.ConstrainedNativeRecords);
        original.Check();
    }

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Length).IsEqualTo(expected.Mutations.Length);
        for (var index = 0; index < expected.Mutations.Length; index++)
        {
            var expectedReferences = expected.Mutations[index].CompositionReferences;
            var actualReferences = actual.Mutations[index].CompositionReferences;
            await Assert.That(actualReferences.Length).IsEqualTo(expectedReferences.Length);
            await Assert.That(actualReferences.SequenceEqual(expectedReferences)).IsTrue();
        }
    }

    internal static async Task RefusedAsync(RequestCqrsCohortNativeNode[] nodes, PartitionRef partition,
        ReplicatedOperation original, OperationResult refused, long[] positions,
        (long LastIndex, long CommittedIndex)[] cuts, CancellationToken token)
    {
        var expected = new OperationResult(null, ErrorCode.PermissionDenied, P.ScopeDenied);
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(expected, refused);
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            await Assert.That(node.Journal.Database.Store.Position).IsEqualTo(positions[index] + P.SingleStep);
            await Assert.That(node.Journal.Log.State.LastIndex).IsEqualTo(cuts[index].LastIndex + P.SingleStep);
            await Assert.That(node.Journal.Log.State.CommittedIndex).IsEqualTo(cuts[index].CommittedIndex + P.SingleStep);
            var entry = node.Journal.Log.ReadEntry(cuts[index].LastIndex + P.SingleStep);
            await Assert.That(entry?.Operation?.Id).IsEqualTo(original.Id);
            await Assert.That(entry!.Operation!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
            await ReplicaForwardedOriginalWorkAssertions.EqualAsync(expected,
                OutcomeStoreOracle.ReadPartition(node.Journal.Database.Store, partition, P.Writer, original.Id));
            await Assert.That(node.Journal.Database.GetDocument(ReplicaForwardedOriginalWorkProtocol.Root,
                new(partition, ReplicaForwardedOriginalWorkProtocol.Collection, ReplicaForwardedOriginalWorkProtocol.Healthy),
                cancellationToken: token)).IsNull();
            var outbox = node.Journal.Database.GetOutboxStatus(ReplicaForwardedOriginalWorkProtocol.Root, partition);
            await Assert.That(outbox.Head.Tail).IsEqualTo(P.FirstRevision);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(P.FirstRevision);
        }
    }

    internal static async Task UnchangedAsync(RequestCqrsCohortNativeNode[] nodes,
        (long Position, string[] Records)[] images, (long LastIndex, long CommittedIndex)[] cuts)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(nodes[index], images[index]);
            await Assert.That(nodes[index].Journal.Log.State.LastIndex).IsEqualTo(cuts[index].LastIndex);
            await Assert.That(nodes[index].Journal.Log.State.CommittedIndex).IsEqualTo(cuts[index].CommittedIndex);
        }
    }
}
