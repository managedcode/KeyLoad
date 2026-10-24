using System.Collections.Immutable;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class TargetInboxCapacityAuthorityHealthy
{
    private const long HealthyEventOutboxSequence = 5L;
    private const int AppendOrdinal = 1;
    private const long HealthyStreamRevision = 1L;
    private const long HealthyStreamGeneration = 1L;
    private const long FirstRetainedRevision = 1L;
    private const long HealthyEventSequence = 2L;
    private const string AppendKind = "appendEvents";
    private const string OutboxFamily = "outbox";
    private const string MissingProducerWitness = "The healthy producer append witness is absent.";

    internal static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        TargetInboxCapacityAuthorityState state, CommitInboxResult result, long expectedPosition)
    {
        var request = state.Healthy;
        var receipt = result.Receipt;
        await Assert.That(result.AlreadyProcessed).IsFalse();
        await Assert.That(result.OriginalEffectsToken).IsEqualTo(receipt.Token);
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Target.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(expectedPosition);

        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        ImmutableArray<MutationReceipt> expected = [new("putDocument", TargetInboxUnitProtocol.Collection, TargetInboxCapacityAuthorityFixture.Healthy, 1L),
            new("appendEvents", TargetInboxUnitProtocol.Events, TargetInboxCapacityAuthorityFixture.Healthy, 1L),
            new("enqueue", TargetInboxUnitProtocol.Output, TargetInboxCapacityAuthorityFixture.Healthy, 1L)];
        await TargetInboxValueAssertions.EqualAsync(receipt.Mutations, expected);
        var document = database.GetDocument(TargetInboxUnitProtocol.Root,
            new(request.Target.Partition, TargetInboxUnitProtocol.Collection, TargetInboxCapacityAuthorityFixture.Healthy));
        await Assert.That(JsonDefaults.Serialize(document)).IsEquivalentTo(JsonDefaults.Serialize(new DocumentResult(
            state.HealthyReference,
            1L, TargetInboxUnitProtocol.Payload, false, [])), CollectionOrdering.Matching);
        await EventAsync(database, store, state, receipt);
        var message = database.InspectMessage(TargetInboxUnitProtocol.Root,
            new(request.Target.Partition, TargetInboxUnitProtocol.Output), TargetInboxCapacityAuthorityFixture.Healthy);
        await Assert.That(JsonDefaults.Serialize(message)).IsEquivalentTo(JsonDefaults.Serialize(new MessageInspection(
            new(TargetInboxCapacityAuthorityFixture.Healthy, MessageState.Ready, 0, 1L, 2L, null, null),
            TargetInboxUnitProtocol.Payload, "{}")), CollectionOrdering.Matching);
        await TargetInboxAssertions.CapacityAsync(store, request, TargetInboxCapacityAuthorityFixture.Capacity);
        await TargetInboxCapacityAuthorityAssertions.SourceAsync(database, state);
    }

    private static async Task EventAsync(DatabaseEngine database, ZoneTreeStore store,
        TargetInboxCapacityAuthorityState state, CommitReceipt receipt)
    {
        var partition = state.Healthy.Target.Partition;
        var producer = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(OutboxFamily,
            partition, HealthyEventOutboxSequence))) ?? throw new InvalidOperationException(MissingProducerWitness);
        await ExactAsync(producer.Commit, receipt.Token);
        await Assert.That(producer.Sequence).IsEqualTo(HealthyEventOutboxSequence);
        await Assert.That(producer.Ordinal).IsEqualTo(AppendOrdinal);
        await ExactAsync(producer.Mutation, state.Healthy.Effects[AppendOrdinal]);
        await ExactAsync(producer.Mutation, new AppendEvents(TargetInboxUnitProtocol.Events,
            TargetInboxCapacityAuthorityFixture.Healthy,
            [new(TargetInboxCapacityAuthorityFixture.Healthy, TargetInboxUnitProtocol.EventType,
                TargetInboxUnitProtocol.Payload)], ExpectedStreamRevision.NoStream));
        await ExactAsync(producer.Receipt, new MutationReceipt(AppendKind, TargetInboxUnitProtocol.Events,
            TargetInboxCapacityAuthorityFixture.Healthy, TargetInboxUnitProtocol.FirstRevision));
        var stream = new StreamRef(partition, TargetInboxUnitProtocol.Events, TargetInboxCapacityAuthorityFixture.Healthy);
        var events = database.ReadStream(TargetInboxUnitProtocol.Root, stream);
        await ExactAsync(events.Head, new StreamHead(HealthyStreamRevision, HealthyStreamGeneration, FirstRetainedRevision));
        await Assert.That(events.HasMore).IsFalse();
        var item = await Assert.That(events.Events).HasSingleItem();
        await ExactAsync(item, new EventRecord(stream, HealthyStreamRevision, HealthyEventSequence,
            new(TargetInboxCapacityAuthorityFixture.Healthy, TargetInboxUnitProtocol.EventType,
                TargetInboxUnitProtocol.Payload), producer.CommittedAt));
    }

    private static async Task ExactAsync<T>(T actual, T expected)
        => await TargetInboxValueAssertions.EqualAsync(actual, expected);

}
