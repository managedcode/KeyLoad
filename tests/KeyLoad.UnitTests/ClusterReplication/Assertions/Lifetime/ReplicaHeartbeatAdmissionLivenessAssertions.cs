using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Replication;
using KeyLoad.Storage;
using D = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaHeartbeatAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaHeartbeatAdmissionLivenessAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static async Task HeldAsync(RequestCqrsCohortRuntimeFixture fixture, RequestCqrsCohortNativeNode leader,
        Task<OperationResult> submitted, ReplicaNodeState before,
        IReadOnlyDictionary<string, (long Position, string[] Records)> images, CancellationToken token)
    {
        var configuration = leader.Journal.Configuration;
        var hold = configuration.UpperElectionTimeout + configuration.RpcTimeout + configuration.HeartbeatInterval;
        var started = TimeProvider.System.GetTimestamp();
        do
        {
            await Assert.That(submitted.IsCompleted).IsFalse();
            foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
            {
                var node = fixture.Node(voter);
                var state = await node.Journal.Consensus.StateAsync(token);
                await Assert.That(state.Term).IsEqualTo(before.Term);
                await Assert.That(state.LeaderId).IsEqualTo(leader.Journal.Configuration.LocalId);
                await Assert.That(state.TransportReady).IsTrue();
                await Assert.That(state.LastIndex).IsEqualTo(before.LastIndex);
                await Assert.That(state.CommittedIndex).IsEqualTo(before.CommittedIndex);
                await Assert.That(state.MaterializedPosition).IsEqualTo(before.MaterializedPosition);
                await ReplicaBarrierDeadlineAssertions.UnchangedAsync(node, images[voter]);
            }
            await Assert.That(await leader.Journal.Consensus.IsLeaderAsync(token)).IsTrue();
            await Task.Delay(configuration.HeartbeatInterval, TimeProvider.System, token);
        } while (TimeProvider.System.GetElapsedTime(started) <= hold);
        await Assert.That(submitted.IsCompleted).IsFalse();
    }

    internal static async Task CommittedAsync(RequestCqrsCohortRuntimeFixture fixture, RequestCqrsCohortNativeNode leader,
        ReplicatedOperation original, CommitReceipt actual, ResourceLifetimeV1 lifetime,
        long index, long revision, string json, CancellationToken token)
    {
        var owner = leader.Hosted!.PhysicalOwner;
        var expected = new CommitReceipt(original.Id, new(owner.Incarnation, D.Partition.AtomicPartitionId, index, owner.PlacementEpoch),
            [new(D.PutKind, D.Collection, D.Document, revision)], DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(expected, actual);
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            var entry = node.Journal.Log.ReadEntry(index)!;
            await Assert.That(entry.Operation!.Id).IsEqualTo(original.Id);
            await Assert.That(entry.Index).IsEqualTo(index);
            var reference = new EntityRef(D.Partition, D.Collection, D.Document) { Lifetime = new(lifetime.Incarnation.Value) };
            await EqualAsync(new DocumentResult(reference, revision, json, false, []),
                node.Journal.Database.GetDocument(D.Root, reference, cancellationToken: token));
            var record = node.Journal.Database.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)));
            await EqualAsync(new DocumentRecord(reference, revision, json, new(), entry.Operation.EvaluatedAt), record!);
            var status = node.Journal.Database.GetOutboxStatus(D.Root, D.Partition);
            await Assert.That(status.Head.Tail).IsEqualTo(revision);
            await Assert.That(status.Head.StoredRecords).IsEqualTo(revision);
            await Assert.That(status.Head.FirstAvailable).IsEqualTo(D.FirstRevision);
            await Assert.That(status.Head.StoredBytes).IsGreaterThan((long)D.Empty);
            await Assert.That(status.Consumers.IsEmpty).IsTrue();
        }
    }

    internal static async Task ReplayAndAdvanceAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortNativeNode leader, ReplicatedOperation original, CommitReceipt receipt,
        ResourceLifetimeV1 lifetime, CancellationToken token)
    {
        var images = ReplicaBarrierDeadlineFlow.Voters.ToDictionary(voter => voter,
            voter => ReplicaBarrierDeadlineAssertions.Image(fixture.Node(voter)), StringComparer.Ordinal);
        var terms = ReplicaBarrierDeadlineFlow.Voters.ToDictionary(voter => voter,
            voter => fixture.Node(voter).Journal.Log.State.Term, StringComparer.Ordinal);
        await EqualAsync(receipt, (await leader.Journal.Consensus.SubmitAsync(original, token)).Get<CommitReceipt>());
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            await ReplicaBarrierDeadlineAssertions.ReplayStateAsync(fixture.Node(voter), images[voter],
                original.Id, receipt.Token.Position, terms[voter]);
        }
        var database = leader.Journal.Database;
        var request = new CommandRequest(Guid.NewGuid(), D.Partition,
            [new PutDocument(D.Collection, D.Document, D.SecondJson, D.FirstRevision)]);
        var operation = database.CreateNativeOperation(OperationKind.Batch, request.CommandId, D.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var index = leader.Journal.Log.State.LastIndex + D.One;
        var actual = (await leader.Hosted!.Coordinator.SubmitVerifiedAsync(operation, token)).Get<CommitReceipt>();
        await CommittedAsync(fixture, leader, operation, actual, lifetime, index, D.SecondRevision, D.SecondJson, token);
    }
}
