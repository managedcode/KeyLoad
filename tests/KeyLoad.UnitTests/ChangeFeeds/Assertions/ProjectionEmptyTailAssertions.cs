using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionEmptyTailAssertions
{
    internal const string Root = "root";
    internal const string Source = "orders";
    internal const string Target = "derived";
    internal const string ConsumerName = "empty-tail-projection";
    internal const string EntrySpace = "outbox";
    internal const string MissingEntry = "The whole operation requires its original committed source entry.";
    internal const string EmptyDetail = "An empty projection batch cannot produce effects.";

    internal static string[] Image(ZoneTreeStore store)
        => global::KeyLoad.UnitTests.Features.GraphTraversal.GraphShortestPathFullStoreImage.Bytes(store);

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, Command<DatabaseCommand> original,
        Result<object> expected, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        await ExactAsync(owner.ApplyEmbedded(original, token), expected);
        await Assert.That(Image(store)).IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task<Result<object>> RejectEffectsAsync(DatabaseEngine owner, ZoneTreeStore store,
        PartitionRef partition, Command<DatabaseCommand> rejected, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        var key = OutcomeStoreOracle.PartitionKey(partition, Root, rejected.CommandId);
        var locatorKey = KeySpace.OutcomeLocatorV2(partition, Root, rejected.CommandId);
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))).IsNull();
        await Assert.That(store.Read(view => view.ReadOwnedValue(locatorKey))).IsNull();
        var result = owner.ApplyEmbedded(rejected, token);
        await ExactAsync(result, Result<object>.Fail(Errors.Problem(ErrorCode.Validation, EmptyDetail)));
        var prefix = Convert.ToHexString(key) + ":";
        var locatorPrefix = Convert.ToHexString(locatorKey) + ":";
        var after = Image(store);
        await Assert.That(after.Count(row => row.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(after.Count(row => row.StartsWith(locatorPrefix, StringComparison.Ordinal))).IsEqualTo(1);
        var locator = store.Read(view => view.ReadOwnedValue(locatorKey)) ?? throw new InvalidOperationException(MissingEntry);
        await Assert.That(locator.AsSpan().SequenceEqual(key)).IsTrue();
        await Assert.That(after.Where(row => !row.StartsWith(prefix, StringComparison.Ordinal)
            && !row.StartsWith(locatorPrefix, StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position + 1L);
        await ExactAsync(OutcomeStoreOracle.ReadPartition(store, partition, Root, rejected.CommandId), result);
        return result;
    }

    internal static async Task ConsumerAsync(DatabaseEngine owner, ZoneTreeStore store,
        ProjectionConsumerRef consumer, ProjectionConsumerDefinition definition, long checkpoint, long tail)
    {
        var total = 0L;
        for (var sequence = 1L; sequence <= tail; sequence++)
        {
            var bytes = store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EntrySpace, consumer.Partition, sequence)))
                ?? throw new InvalidOperationException(MissingEntry);
            total += bytes.LongLength;
        }
        await ExactAsync(owner.GetOutboxStatus(Root, consumer.Partition),
            new OutboxStatus(new(tail, 1L, tail, total), [new(consumer, definition, checkpoint, false)]));
    }

    internal static async Task DocumentsAsync(DatabaseEngine owner, PartitionRef partition, bool effect)
    {
        await ExactAsync(owner.GetDocument(Root, new(partition, Source, "input")),
            new DocumentResult(new(partition, Source, "input"), 1L, "{\"n\":1}", false, []));
        if (effect)
        {
            await ExactAsync(owner.GetDocument(Root, new(partition, Target, "effect")),
                new DocumentResult(new(partition, Target, "effect"), 1L, "{\"sourceCount\":1}", false, []));
        }
        else
        {
            await Assert.That(owner.GetDocument(Root, new(partition, Target, "effect"))).IsNull();
        }
    }

    internal static async Task ResultAsync(Command<DatabaseCommand> original, Result<object> result, long checkpoint,
        params MutationReceipt[] mutations)
    {
        var actual = result.Get<ProjectionBatchResult>();
        await Assert.That(actual.Receipt.CommandId).IsEqualTo(original.CommandId);
        await ExactAsync(actual.Receipt.Mutations, global::System.Collections.Immutable.ImmutableArray.CreateRange(mutations));
        await Assert.That(actual.AlreadyProcessed).IsFalse();
        await Assert.That(actual.Checkpoint).IsEqualTo(checkpoint);
    }

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
