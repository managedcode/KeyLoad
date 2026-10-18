using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class OutboxHeadAuthorityAssertions
{
    internal static string[] Image(ZoneTreeStore store)
        => global::KeyLoad.UnitTests.CanonicalStoreImage.Bytes(store);

    internal static async Task SameAsync(ZoneTreeStore store, string[] image, long position)
    {
        await Assert.That(Image(store)).IsEquivalentTo(image, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        OutboxHeadAuthorityState state, int fault, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.ApplyEmbedded(state.Purge, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(fault is not (4 or 5) ? OutboxHeadAuthorityFixture.HeadDetail : OutboxHeadAuthorityFixture.BytesDetail);
        await SameAsync(store, image, position);
        if (fault is not (4 or 5))
        {
            var writer = OutboxHeadAuthorityFixture.Write(owner, state.Partition, "d");
            var producer = Assert.ThrowsExactly<KeyLoadException>(() => owner.ApplyEmbedded(writer, token));
            await Assert.That(producer.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(producer.Message).IsEqualTo(OutboxHeadAuthorityFixture.HeadDetail);
            await SameAsync(store, image, position);
        }
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        Command<DatabaseCommand> operation, Result<object> expected, CancellationToken token)
    {
        var image = Image(store);
        var position = store.Position;
        await ExactAsync(owner.ApplyEmbedded(operation, token), expected);
        await SameAsync(store, image, position);
    }

    internal static async Task ReceiptAsync(Command<DatabaseCommand> command, CommitReceipt receipt, params MutationReceipt[] expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await ExactAsync(receipt.Mutations, global::System.Collections.Immutable.ImmutableArray.CreateRange(expected));
    }

    internal static async Task DocumentsAsync(DatabaseEngine owner, PartitionRef partition, bool continued)
    {
        await ExactAsync(owner.GetDocument(OutboxHeadAuthorityFixture.Root, new(partition, OutboxHeadAuthorityFixture.Collection, "a")),
            new DocumentResult(new(partition, OutboxHeadAuthorityFixture.Collection, "a"), 1L, "{\"n\":1}", false, []));
        await ExactAsync(owner.GetDocument(OutboxHeadAuthorityFixture.Root, new(partition, OutboxHeadAuthorityFixture.Collection, "b")),
            new DocumentResult(new(partition, OutboxHeadAuthorityFixture.Collection, "b"), 1L, "{\"n\":2}", false, []));
        if (continued)
        {
            await ExactAsync(owner.GetDocument(OutboxHeadAuthorityFixture.Root, new(partition, OutboxHeadAuthorityFixture.Collection, "c")),
                new DocumentResult(new(partition, OutboxHeadAuthorityFixture.Collection, "c"), 1L, "{\"n\":3}", false, []));
        }
        else
        {
            await Assert.That(owner.GetDocument(OutboxHeadAuthorityFixture.Root, new(partition, OutboxHeadAuthorityFixture.Collection, "c"))).IsNull();
        }
    }

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
