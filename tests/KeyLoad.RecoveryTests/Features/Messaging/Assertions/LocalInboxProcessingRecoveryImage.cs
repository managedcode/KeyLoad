using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryImage
{
    private const int InventoryBound = 4096;

    internal static byte[] InboxKey() => KeySpace.Partition("inbox", LocalInboxProcessingCrashContract.Partition,
        LocalInboxProcessingCrashContract.Input, LocalInboxProcessingCrashContract.Message,
        LocalInboxProcessingCrashContract.Handler, LocalInboxProcessingCrashContract.Generation);

    internal static string[] All(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], InventoryBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete local processing fixture exceeded its bounded native inventory.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        ReplicatedOperation operation, OperationResult expected)
    {
        var image = All(store);
        var position = store.Position;
        await Assert.That(NativeSerialization.Serialize(database.Apply(operation)))
            .IsEquivalentTo(NativeSerialization.Serialize(expected), CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(All(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task CutAsync(DatabaseEngine database, ZoneTreeStore store, ReplicatedOperation original,
        MessageInspection input, long seedPosition, long seedTail, bool committed)
    {
        await Assert.That(store.Position).IsEqualTo(seedPosition + (committed ? 1L : 0L));
        var record = store.Read(view => view.GetRecord<InboxRecord>(InboxKey()));
        await Assert.That(record is not null).IsEqualTo(committed);
        await Assert.That(OutcomeStoreOracle.Read(store, original) is not null).IsEqualTo(committed);
        var expectedInput = committed ? input with
        {
            Metadata = input.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = LocalInboxProcessingCrashContract.FinalVersion,
                LeaseOwner = null,
                LeaseUntil = null
            },
            PayloadJson = null,
            HeadersJson = null
        } : input;
        await ExactAsync(database.InspectMessage(CrashFixtureValues.Principal,
            LocalInboxProcessingCrashContract.InputLane, LocalInboxProcessingCrashContract.Message), expectedInput);
        await LocalInboxProcessingRecoveryModels.RequireAsync(database, original.EvaluatedAt, committed);
        var head = database.GetOutboxStatus(CrashFixtureValues.Principal, LocalInboxProcessingCrashContract.Partition).Head;
        await Assert.That(head.Tail).IsEqualTo(seedTail + (committed ? LocalInboxProcessingCrashContract.MutationCount : 0));
        for (var ordinal = 0; ordinal < LocalInboxProcessingCrashContract.MutationCount; ordinal++)
        {
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox",
                LocalInboxProcessingCrashContract.Partition, seedTail + ordinal + 1L))) is not null).IsEqualTo(committed);
        }
    }
}
