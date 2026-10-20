using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingState;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingAssertions
{
    internal static CommitReceipt Receipt(ZoneTreeStore store, PartitionRef partition, Guid id, long index, MutationReceipt[] mutations)
        => new(id, new(store.Identity.Incarnation, partition.AtomicPartitionId, index), [.. mutations],
            DurabilityProfile.ProcessDurable, PersistedCopies);

    internal static async Task FailureDeltaAsync(ZoneTreeStore store, string[] before, PartitionRef partition, Command<DatabaseCommand> original)
    {
        var locator = KeySpace.OutcomeLocator(partition, original.UserId!, original.CommandId);
        await Assert.That(ResourceIncarnationAssertions.Bytes(store, locator).AsSpan()
            .SequenceEqual(KeySpace.PartitionOutcome(partition, original.UserId!, original.CommandId))).IsTrue();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes), Convert.ToHexString(locator),
            Convert.ToHexString(KeySpace.PartitionOutcome(partition, original.UserId!, original.CommandId))
        };
        await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(store)
            .Where(row => !allowed.Contains(row.Split(RowSeparator, StringSplitOptions.None)[Zero])).ToArray())
            .IsEquivalentTo(before.Where(row => !allowed.Contains(row.Split(RowSeparator, StringSplitOptions.None)[Zero])).ToArray(),
                global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task PairsAsync(ResourcePairBindingState state, ZoneTreeStore store)
    {
        foreach (var pair in state.Pairs)
        {
            await Assert.That(ResourceIncarnationAssertions.Bytes(store, KeySpace.Resource(state.Source.Partition.TenantId,
                state.Source.Partition.DatabaseId, pair.Key)).AsSpan().SequenceEqual(pair.Value.Definition)).IsTrue();
            await Assert.That(ResourceIncarnationAssertions.Bytes(store,
                ResourcePairBindingFlow.LifetimeKey(state.Source.Partition, pair.Key)).AsSpan().SequenceEqual(pair.Value.Lifetime)).IsTrue();
        }
    }

    internal static async Task ModelsAsync(ResourcePairBindingState state, DatabaseEngine database)
    {
        foreach (var value in state.Mixed)
        {
            await ResourceIncarnationAssertions.EqualAsync(new DocumentResult(state.Source.ExpectedReference(new(state.Source.Partition, Documents, value.Id)), FirstRevision, DocumentJson, false, []),
                database.GetDocument(Root, new(state.Source.Partition, Documents, value.Id))!);
            var stream = new StreamRef(state.Source.Partition, Events, value.Id);
            var page = database.ReadStream(Root, stream);
            await ResourceIncarnationAssertions.EqualAsync(new StreamHead(FirstRevision, FirstRevision, FirstRevision), page.Head);
            await ResourceIncarnationAssertions.EqualAsync(new EventRecord(stream, FirstRevision, value.Sequence,
                new(value.Id, EventType, StoredEventJson), value.Time), page.Events.Single());
            await ResourceIncarnationAssertions.EqualAsync(new StreamPage(stream, new(FirstRevision, FirstRevision, FirstRevision),
                [new(stream, FirstRevision, value.Sequence, new(value.Id, EventType, StoredEventJson), value.Time)],
                database.LastApplied, false, null, database.LastApplied), page);
            await ResourceIncarnationAssertions.EqualAsync(new MessageInspection(new(value.Id, MessageState.Ready, Zero, FirstRevision, value.Sequence, null, null),
                StoredQueueJson, Headers), database.InspectMessage(Root, new(state.Source.Partition, Queue), value.Id)!);
        }
        foreach (var row in state.Rows)
        {
            await ResourceIncarnationAssertions.EqualAsync(new DocumentResult(state.Source.ExpectedReference(new(state.Source.Partition, Table, row.Id)), FirstRevision, row.Json, false, []),
                database.GetDocument(Root, new(state.Source.Partition, Table, row.Id))!);
        }
    }
}
