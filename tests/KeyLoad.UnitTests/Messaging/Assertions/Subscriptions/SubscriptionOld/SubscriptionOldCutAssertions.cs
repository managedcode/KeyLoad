using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionOldCutAssertions
{
    internal static async Task SameAsync<T>(T actual, T expected)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task StatusAsync(DatabaseEngine database, SubscriptionOldCutState state,
        long generation, long checkpoint, long issued, long tail, bool paused = false)
    {
        await SameAsync(database.GetSubscription("root", state.Group),
            new SubscriptionInfo(state.Group, state.Definition, generation, generation, checkpoint, issued, tail, paused, null));
        var page = database.ReadEventSource("root", new(state.Group.Source));
        await Assert.That(page.Source).IsEqualTo(state.Group.Source);
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(tail, 1, 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Events.Length).IsEqualTo((int)tail);
        foreach (var position in Enumerable.Range(1, (int)tail))
        {
            await Assert.That(page.Events[position - 1]).IsEqualTo(new SourceEventRecord(state.Group.Source,
                position, position, new("event" + position, "Created", "{\"n\":" + position + "}"),
                position == 4 ? SubscriptionOldCutState.Epoch.AddSeconds(31) : SubscriptionOldCutState.Epoch));
        }
        await SameAsync(database.GetDocument("root", new(state.Group.Source.Partition,
            SubscriptionOldCutState.Collection, "input2")), new DocumentResult(
                new(state.Group.Source.Partition, SubscriptionOldCutState.Collection, "input2") { Lifetime = state.DocumentLifetime },
                1, SubscriptionOldCutState.EffectJson, false, []));
    }
    internal static async Task ConservedAsync(ZoneTreeStore store, string[] bytes, long position, StoreIdentity identity)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, identity, position);
    }
    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid id, ZoneTreeStore store, PartitionRef partition,
        long position, params MutationReceipt[] mutations)
    {
        await SameAsync(receipt, new CommitReceipt(id, new(store.Identity.Incarnation, partition.AtomicPartitionId, position), [.. mutations], DurabilityProfile.ProcessDurable, 1));
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
}
