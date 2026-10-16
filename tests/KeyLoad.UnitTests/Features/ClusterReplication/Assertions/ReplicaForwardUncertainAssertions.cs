using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardUncertainProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUncertainAssertions
{
    internal static async Task FailureAsync(ReplicaForwardUncertainObservation observation,
        ReplicatedOperation original, ReadExecutionBudget work, KeyLoadException failure, CancellationToken token)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(failure.InnerException?.GetType()).IsEqualTo(observation.Fault.GetType());
        await Assert.That(failure.InnerException?.Message).IsEqualTo(P.Sentinel);
        await Assert.That(observation.CompletedForOriginal).IsEqualTo(P.One);
        await Assert.That(observation.ReceiversFor(original.Id)).IsEqualTo(P.One);
        var receiver = observation.Receiver ?? throw new InvalidOperationException(P.Missing);
        var settled = await ((NativeRequestWorkLease)receiver.Binding).Settlement.WaitAsync(token);
        await Assert.That(ReferenceEquals(settled.OriginalFailure, observation.Fault)).IsTrue();
        await Assert.That(settled.CleanupOrJoinFailure).IsNull();
        await Assert.That(receiver.Budget.ReadBytes > P.Zero).IsTrue();
        await Assert.That(receiver.Budget.ConstrainedNativeRecords > P.Zero).IsTrue();
        await Assert.That(work.HasIndeterminateMetadata).IsTrue();
        await Assert.That(work.ReadBytes).IsEqualTo(observation.SourceBytesBeforeExecute);
        await Assert.That(work.ConstrainedNativeRecords).IsEqualTo(observation.SourceRecordsBeforeExecute);
        await Assert.That(work.RemainingReadGrantBytes).IsEqualTo(P.NoBytes);
        await Assert.That(work.RemainingReadGrantRecords).IsEqualTo(P.Zero);
        var check = Assert.ThrowsExactly<KeyLoadException>(work.Check);
        await Assert.That(ReferenceEquals(check, failure)).IsTrue();
        var copied = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(
            observation.OriginalBytes ?? throw new InvalidOperationException(P.Missing));
        await Assert.That(copied.Id).IsEqualTo(original.Id);
        await Assert.That(copied.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        var reply = ReplicaProtocolCodec.Deserialize<ReplicaForwardReplyV1>(
            observation.ReplyBytes ?? throw new InvalidOperationException(P.Missing));
        ReplicaForwardFrameValidation.RequireReply(reply, budgeted: true);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.Statistics).IsNotNull();
        var result = ReplicaProtocolCodec.Deserialize<OperationResult>(reply.Result.Span);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(original.Id);
        var grantFailure = Assert.ThrowsExactly<KeyLoadException>(() => work.CreateReadGrant(P.One, P.One));
        await Assert.That(ReferenceEquals(grantFailure, failure)).IsTrue();
    }

    internal static async Task PublishedAsync(ReplicaForwardUncertainObservation observation, CommitReceipt expected)
    {
        var reply = ReplicaProtocolCodec.Deserialize<ReplicaForwardReplyV1>(
            observation.ReplyBytes ?? throw new InvalidOperationException(P.Missing));
        var result = ReplicaProtocolCodec.Deserialize<OperationResult>(reply.Result.Span);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, result.Get<CommitReceipt>());
    }

    internal static async Task SingleCommitAsync(RequestCqrsCohortNativeNode[] nodes, long[] positions, long index)
    {
        for (var item = P.Zero; item < nodes.Length; item++)
        {
            await Assert.That(nodes[item].Journal.Database.Store.Position).IsEqualTo(positions[item] + P.One);
            await Assert.That(nodes[item].Journal.Log.State.LastIndex).IsEqualTo(index);
            await Assert.That(nodes[item].Journal.Log.State.CommittedIndex).IsEqualTo(index);
        }
    }

    internal static async Task StateAsync(RequestCqrsCohortNativeNode[] nodes, EntityRef reference,
        CommitReceipt expected, ReplicatedOperation original, long expectedOutbox, CancellationToken token)
    {
        foreach (var node in nodes)
        {
            var retained = OutcomeStoreOracle.ReadPartition(node.Journal.Database.Store,
                reference.Partition, original.PrincipalId, original.Id);
            await Assert.That(retained).IsNotNull();
            await Assert.That(retained!.Error).IsNull();
            await Assert.That(retained.SafeDetail).IsNull();
            await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, retained.Get<CommitReceipt>());
            await ReplicaForwardedOriginalWorkAssertions.EqualAsync<DocumentResult?>(
                new(reference, P.InitialRevision, P.Json, false, []),
                node.Journal.Database.GetDocument(P.Root, reference, cancellationToken: token));
            var outbox = node.Journal.Database.GetOutboxStatus(P.Root, reference.Partition);
            await Assert.That(outbox.Head.Tail).IsEqualTo(expectedOutbox);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(expectedOutbox);
            var entry = node.Journal.Log.ReadEntry(expected.Token.Position);
            await Assert.That(entry?.Operation?.Id).IsEqualTo(original.Id);
            await Assert.That(entry?.Operation?.Kind).IsEqualTo(OperationKind.Batch);
            await Assert.That(entry?.Operation?.PrincipalId).IsEqualTo(original.PrincipalId);
            await Assert.That(entry!.Operation!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        }
    }
}
