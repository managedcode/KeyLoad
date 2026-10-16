using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardMetadataProtocol;
using W = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaForwardedOriginalWorkProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardMetadataSuccess
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture,
        RequestCqrsCohortNativeNode[] nodes, RequestCqrsCohortNativeNode leader,
        RequestCqrsCohortNativeNode follower, ReplicatedOperation original, CommandRequest command, EntityRef expectedReference,
        ReadExecutionBudget work, ReplicaForwardMetadataObservation observation, CancellationToken token)
    {
        var expiry = work.ExpiresAt;
        var expectedIndex = checked(leader.Journal.Log.State.LastIndex + P.SingleStep);
        var first = await ReplicaForwardMetadataFlow.SubmitAsync(follower, original, work, observation, token);
        var receipt = first.Get<CommitReceipt>();
        await Assert.That(observation.CountFor(original.Id)).IsEqualTo(P.SingleStep);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.For(work, original.Id), work, token);
        await Assert.That(work.ExpiresAt).IsEqualTo(expiry);
        var expected = new CommitReceipt(original.Id,
            new(leader.Journal.Database.Store.Identity.Incarnation, command.Partition.AtomicPartitionId,
                expectedIndex, P.FirstRevision),
            [new(W.PutKind, W.Collection, W.Healthy, P.FirstRevision)], DurabilityProfile.QuorumProcessDurable);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, receipt);
        var entry = leader.Journal.Log.ReadEntry(expectedIndex);
        await Assert.That(entry?.Operation?.Id).IsEqualTo(original.Id);
        await Assert.That(entry?.Operation?.PrincipalId).IsEqualTo(P.Writer);
        await Assert.That(entry?.Operation?.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(entry!.Operation!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(command, NativeCommandPayload.Read<CommandRequest>(entry.Operation));
        await ReplicaForwardedOriginalWorkNodes.BarrierAsync(nodes, token);
        await StateAsync(nodes, expectedReference, original.Id, expected, token);
        var images = nodes.Select(ReplicaBarrierDeadlineAssertions.Image).ToArray();
        var cuts = nodes.Select(node => (node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex)).ToArray();
        var replay = await ReplicaForwardMetadataFlow.SubmitAsync(follower, original, work, observation, token);
        await ReplicaForwardMetadataAssertions.ImportedAsync(observation.For(work, original.Id), work, token);
        await Assert.That(observation.CountFor(original.Id)).IsEqualTo(P.SecondForward);
        await ReplicaForwardedOriginalWorkAssertions.EqualAsync(first, replay);
        await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, replay.Get<CommitReceipt>());
        await ReplicaForwardMetadataAssertions.UnchangedAsync(nodes, images, cuts);
        await StateAsync(nodes, expectedReference, original.Id, expected, token);
        await Assert.That(work.ExpiresAt).IsEqualTo(expiry);
        work.Check();
    }

    private static async Task StateAsync(RequestCqrsCohortNativeNode[] nodes, EntityRef expectedReference,
        Guid id, CommitReceipt expected, CancellationToken token)
    {
        foreach (var node in nodes)
        {
            var database = node.Journal.Database;
            var retained = OutcomeStoreOracle.ReadPartition(database.Store, expectedReference.Partition, P.Writer, id);
            await Assert.That(retained).IsNotNull();
            await Assert.That(retained!.Error).IsNull();
            await Assert.That(retained.SafeDetail).IsNull();
            await ReplicaForwardMetadataAssertions.ReceiptAsync(expected, retained.Get<CommitReceipt>());
            await ReplicaForwardedOriginalWorkAssertions.EqualAsync<DocumentResult?>(
                new DocumentResult(expectedReference, P.FirstRevision, W.HealthyJson, false, []),
                database.GetDocument(W.Root, new(expectedReference.Partition, W.Collection, W.Healthy), cancellationToken: token));
        }
    }
}
