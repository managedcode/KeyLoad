using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextProtectedRebuildColdAssertions
{
    internal static async Task LiteralAsync(TextProtectedRebuildColdOwner owner, string term,
        string id, long revision, string publicJson, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        var rows = await owner.Search.SearchAsync(TextProtectedRebuildColdSeed.Reader,
            Request(owner, term), token);
        var expected = new RankedDocument(new(owner.Reference(new(owner.Source.Partition,
            TextProtectedRebuildColdSeed.Collection, id)), revision, publicJson, true,
            [TextProtectedRebuildColdSeed.Field]), 1d / 61d);
        await Assert.That(JsonDefaults.Serialize(rows).SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
        await UnchangedAsync(owner, before, cut);
    }

    internal static async Task CurrentAsync(TextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        await LiteralAsync(owner, "оновлено", "alpha", 2L, "{\"other\":\"alpha\"}", token);
        foreach (var term in new[] { "ПРИВІТ", "hello" })
        {
            var before = QueueWholeFlowStorage.Bytes(owner.Store);
            var cut = owner.Store.Position;
            await Assert.That(await owner.Search.SearchAsync(TextProtectedRebuildColdSeed.Reader,
                Request(owner, term), token)).IsEmpty();
            await UnchangedAsync(owner, before, cut);
        }
    }

    internal static async Task DeniedAsync(TextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        KeyLoadException? refusal = null;
        try
        {
            await owner.Search.SearchAsync(TextProtectedRebuildColdSeed.Reader,
                Request(owner, "оновлено"), token);
        }
        catch (KeyLoadException error)
        {
            refusal = error;
        }
        if (refusal is null)
        {
            throw new InvalidOperationException("The genuine protected query did not refuse.");
        }
        await Assert.That(refusal.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await UnchangedAsync(owner, before, cut);
    }

    internal static async Task ReplayAsync(TextProtectedRebuildColdOwner owner,
        TextProtectedRebuildColdOwner.Retained original, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        var replay = owner.Database.ApplyEmbedded(original.Operation, cancellationToken: token);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(
            OrleansSerialization.Serialize(original.Result))).IsTrue();
        await UnchangedAsync(owner, before, cut);
    }

    internal static async Task ReceiptAsync(TextProtectedRebuildColdOwner owner,
        TextProtectedRebuildColdOwner.Retained original, MutationReceipt[] mutations)
    {
        var receipt = original.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(original.Operation.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(owner.Source.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations.ToArray()).SequenceEqual(
            OrleansSerialization.Serialize(mutations))).IsTrue();
    }

    internal static async Task UnchangedAsync(TextProtectedRebuildColdOwner owner, string[] bytes, long cut)
    {
        await Assert.That(owner.Store.Position).IsEqualTo(cut);
        await Assert.That(OrleansSerialization.Serialize(QueueWholeFlowStorage.Bytes(owner.Store))
            .SequenceEqual(OrleansSerialization.Serialize(bytes))).IsTrue();
    }

    private static SearchRequest Request(TextProtectedRebuildColdOwner owner, string term)
        => new(owner.Source.Partition, TextProtectedRebuildColdSeed.Collection,
            TextProtectedRebuildColdSeed.Field, term, Limit: 2);
}
