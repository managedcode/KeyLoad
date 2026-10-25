using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class RetainedAppendIdentityAssertions
{
    internal static async Task SameAsync<T>(T actual, T expected)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static async Task ReceiptAsync(CommitReceipt actual, Guid id, Guid incarnation,
        PartitionRef partition, long position, MutationReceipt[] effects)
    {
        await SameAsync(actual, new CommitReceipt(id, new(incarnation, partition.AtomicPartitionId, position), [.. effects], DurabilityProfile.ProcessDurable, 1));
        foreach (var effect in actual.Mutations)
        {
            await Assert.That(effect.CompositionReferences).IsEmpty();
        }
    }
    internal static async Task ConservedAsync(ZoneTreeStore store, string[] image, long position, StoreIdentity identity)
    {
        await Assert.That(KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(store, identity, position);
    }
}
