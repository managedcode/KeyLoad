using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingAssertions
{
    internal static async Task IdentityAsync(StoreIdentity expected, StoreIdentity actual)
        => await Assert.That(NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();
    internal static Task EqualAsync<T>(T expected, T actual) => ResourceIncarnationAssertions.EqualAsync(expected, actual);
    internal static Task UnchangedAsync(ZoneTreeStore store, string[] image, long position)
        => ResourceIncarnationAssertions.UnchangedAsync(store, image, position);
    internal static byte[] Outcome(ZoneTreeStore store, PartitionRef partition, ReplicatedOperation original)
        => RequiredProcessingPolicyAssertions.Outcome(store, partition, original.PrincipalId, original.Id);
    internal static CommitReceipt Receipt(ZoneTreeStore store, PartitionRef partition, Guid id, long index, MutationReceipt[] mutations)
        => new(id, new(store.Identity.Incarnation, partition.AtomicPartitionId, index, FirstEpoch), [.. mutations], DurabilityProfile.ProcessDurable);

    internal static void Remember(ResourcePairBindingState state, ReplicatedOperation original, OperationResult result)
        => state.Originals.Add((original, result, Outcome(state.Source.Store, state.Source.Partition, original)));

    internal static async Task FailureDeltaAsync(ZoneTreeStore store, string[] before, PartitionRef partition, ReplicatedOperation original)
    {
        var locator = KeySpace.OutcomeLocatorV2(partition, original.PrincipalId, original.Id);
        await Assert.That(ResourceIncarnationAssertions.Bytes(store, locator).AsSpan()
            .SequenceEqual(KeySpace.PartitionOutcome(partition, original.PrincipalId, original.Id))).IsTrue();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes), Convert.ToHexString(locator),
            Convert.ToHexString(KeySpace.PartitionOutcome(partition, original.PrincipalId, original.Id))
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
                ResourcePairBindingFaults.LifetimeKey(state.Source.Partition, pair.Key)).AsSpan().SequenceEqual(pair.Value.Lifetime)).IsTrue();
        }
    }

    internal static async Task ModelsAsync(ResourcePairBindingState state, DatabaseEngine database)
    {
        foreach (var value in state.Mixed)
        {
            await EqualAsync(new DocumentResult(new(state.Source.Partition, Documents, value.Id), FirstRevision, DocumentJson, false, []),
                database.GetDocument(Root, new(state.Source.Partition, Documents, value.Id))!);
            var stream = new StreamRef(state.Source.Partition, Events, value.Id);
            var page = database.ReadStream(Root, stream);
            await EqualAsync(new StreamHead(FirstRevision, FirstRevision, FirstRevision), page.Head);
            await EqualAsync(new EventRecord(stream, FirstRevision, value.Sequence,
                new(value.Id, EventType, StoredEventJson), value.Time), page.Events.Single());
            await EqualAsync(new StreamPage(stream, new(FirstRevision, FirstRevision, FirstRevision),
                [new(stream, FirstRevision, value.Sequence, new(value.Id, EventType, StoredEventJson), value.Time)],
                database.Store.Position, false, null, database.Store.Position), page);
            await EqualAsync(new MessageInspection(new(value.Id, MessageState.Ready, Zero, FirstRevision, value.Sequence, null, null),
                StoredQueueJson, Headers), database.InspectMessage(Root, new(state.Source.Partition, Queue), value.Id)!);
        }
        foreach (var row in state.Rows)
        {
            await EqualAsync(new DocumentResult(new(state.Source.Partition, Table, row.Id), FirstRevision, row.Json, false, []),
                database.GetDocument(Root, new(state.Source.Partition, Table, row.Id))!);
        }
    }
}
