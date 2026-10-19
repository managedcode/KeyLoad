using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
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
    {
        await QueueEnqueueProcessValueAssertions.EqualAsync(actual, expected);
        if (actual is OutboxEntry entry && expected is OutboxEntry expectedEntry)
        {
            await Assert.That(JsonDefaults.Serialize(entry.Receipt.CompositionReferences))
                .IsEquivalentTo(JsonDefaults.Serialize(expectedEntry.Receipt.CompositionReferences), CollectionOrdering.Matching);
        }
    }

    internal static async Task ResultAsync(Result<object> actual, Result<object> expected)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(expected.Problem));
        await Assert.That(actual.Problem?.Detail).IsEqualTo(expected.Problem?.Detail);
        await Assert.That(actual.Value?.GetType()).IsEqualTo(expected.Value?.GetType());
        await ExactAsync(actual.Value, expected.Value);
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> expected)
    {
        var image = All(store);
        var position = store.Position;
        await ResultAsync(database.Apply(operation), expected);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(All(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task CutAsync(DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> original,
        MessageInspection input, long seedPosition, long seedTail, bool committed, EntityRefLifetimeV1 lifetime)
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
        await LocalInboxProcessingRecoveryModels.RequireAsync(database, new DateTimeOffset(original.Timestamp), committed, lifetime);
        var head = database.GetOutboxStatus(CrashFixtureValues.Principal, LocalInboxProcessingCrashContract.Partition).Head;
        await Assert.That(head.Tail).IsEqualTo(seedTail + (committed ? LocalInboxProcessingCrashContract.MutationCount : 0));
        for (var ordinal = 0; ordinal < LocalInboxProcessingCrashContract.MutationCount; ordinal++)
        {
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox",
                LocalInboxProcessingCrashContract.Partition, seedTail + ordinal + 1L))) is not null).IsEqualTo(committed);
        }
    }
}
