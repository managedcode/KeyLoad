using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaBarrierDeadlineAssertions
{
    private const string MutationKind = "putDocument";

    internal static async Task SettingsAsync(RequestCqrsCohortRuntimeFixture fixture)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var journal = fixture.Node(voter).Journal;
            await Assert.That(journal.Configuration.VoterIds.Length).IsEqualTo(3);
            await Assert.That(journal.Configuration.Majority).IsEqualTo(2);
            await Assert.That(journal.Configuration.RpcTimeout).IsEqualTo(P.RpcTimeout);
            await Assert.That(journal.Configuration.LowerElectionTimeout).IsEqualTo(P.LowerElectionTimeout);
            await Assert.That(journal.Configuration.UpperElectionTimeout).IsEqualTo(P.UpperElectionTimeout);
            await Assert.That(journal.ExecutionOptions.Value.ReadBarrierTimeout).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(journal.ExecutionOptions.Value.CommandTimeout).IsEqualTo(TimeSpan.FromSeconds(20));
            await Assert.That(ReferenceEquals(journal.Log.CanonicalDatabase, journal.Database)).IsTrue();
        }
    }

    internal static async Task ReceiptAsync(RequestCqrsCohortNativeNode leader, Guid commandId,
        long expectedIndex, CommitReceipt actual)
    {
        var expected = new CommitReceipt(commandId,
            new(leader.Journal.Database.Store.Identity.Incarnation, P.Partition.AtomicPartitionId, expectedIndex, 1),
            [new(MutationKind, P.Collection, P.DocumentId, 1)], DurabilityProfile.ProcessDurable);
        await Assert.That(leader.Journal.Database.Store.Identity.Incarnation).IsEqualTo(leader.Journal.Configuration.Incarnation);
        await Assert.That(leader.Journal.Database.Store.Identity.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        await Assert.That(leader.Journal.Database.LastApplied).IsEqualTo(leader.Journal.Log.State.CommittedIndex);
    }

    internal static async Task DocumentAsync(RequestCqrsCohortNativeNode node, CancellationToken token)
    {
        var before = Image(node);
        var actual = node.Journal.Database.GetDocument(P.Root,
            new(P.Partition, P.Collection, P.DocumentId), cancellationToken: token);
        await EqualAsync(new DocumentResult(new(P.Partition, P.Collection, P.DocumentId), 1, P.Json, false, []), actual);
        var outbox = node.Journal.Database.GetOutboxStatus(P.Root, P.Partition);
        await Assert.That(outbox.Head.Tail).IsEqualTo(1L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredBytes > 0).IsTrue();
        await Assert.That(outbox.Consumers.IsEmpty).IsTrue();
        await UnchangedAsync(node, before);
    }

    internal static (long Position, string[] Records) Image(RequestCqrsCohortNativeNode node)
        => (node.Journal.Database.Store.Position, QueueWholeFlowStorage.Bytes((ZoneTreeStore)node.Journal.Database.Store));

    internal static async Task UnchangedAsync(RequestCqrsCohortNativeNode node, (long Position, string[] Records) before)
    {
        var after = Image(node);
        await Assert.That(after.Position).IsEqualTo(before.Position);
        await Assert.That(after.Records.SequenceEqual(before.Records, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task ReplayStateAsync(RequestCqrsCohortNativeNode node,
        (long Position, string[] Records) before, Guid operationId, long expectedIndex, long expectedTerm)
    {
        await UnchangedAsync(node, before);
        await Assert.That(node.Journal.Log.State.LastIndex).IsEqualTo(expectedIndex);
        await Assert.That(node.Journal.Log.State.Term).IsEqualTo(expectedTerm);
        await Assert.That(node.Journal.Log.State.CommittedIndex).IsEqualTo(expectedIndex);
        await Assert.That(node.Journal.Database.LastApplied).IsEqualTo(expectedIndex);
        var entry = node.Journal.Log.ReadEntry(expectedIndex);
        await Assert.That(entry is not null).IsTrue();
        await Assert.That(entry!.Index).IsEqualTo(expectedIndex);
        await Assert.That(entry.Term).IsEqualTo(expectedTerm);
        await Assert.That(entry.Operation is not null).IsTrue();
        await Assert.That(entry.Operation!.Id).IsEqualTo(operationId);
    }

    internal static async Task ReplayAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(expected).SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
}
