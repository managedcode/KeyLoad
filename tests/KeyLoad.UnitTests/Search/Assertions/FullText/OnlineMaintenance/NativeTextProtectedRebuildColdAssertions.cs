using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProtectedRebuildColdAssertions
{
    internal static async Task LiteralAsync(NativeTextProtectedRebuildColdOwner owner, string term,
        string id, long revision, string publicJson, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        var rows = await owner.Search.SearchAsync(NativeTextProtectedRebuildColdSeed.Reader,
            Request(owner, term), token);
        var expected = new RankedDocument(new(new(owner.Source.Partition,
            NativeTextProtectedRebuildColdSeed.Collection, id), revision, publicJson, true,
            [NativeTextProtectedRebuildColdSeed.Field]), 1d / 61d);
        await Assert.That(JsonDefaults.Serialize(rows).SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
        await UnchangedAsync(owner, before, cut);
    }

    internal static async Task CurrentAsync(NativeTextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        await LiteralAsync(owner, "оновлено", "alpha", 2L, "{\"other\":\"alpha\"}", token);
        foreach (var term in new[] { "ПРИВІТ", "hello" })
        {
            var before = QueueWholeFlowStorage.Bytes(owner.Store);
            var cut = owner.Store.Position;
            await Assert.That(await owner.Search.SearchAsync(NativeTextProtectedRebuildColdSeed.Reader,
                Request(owner, term), token)).IsEmpty();
            await UnchangedAsync(owner, before, cut);
        }
    }

    internal static async Task DeniedAsync(NativeTextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        KeyLoadException? refusal = null;
        try
        {
            await owner.Search.SearchAsync(NativeTextProtectedRebuildColdSeed.Reader,
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

    internal static async Task ReplayAsync(NativeTextProtectedRebuildColdOwner owner,
        NativeTextProtectedRebuildColdOwner.Retained original, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(owner.Store);
        var cut = owner.Store.Position;
        var replay = owner.Database.ApplyEmbedded(original.Operation, cancellationToken: token);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(
            NativeSerialization.Serialize(original.Result))).IsTrue();
        await UnchangedAsync(owner, before, cut);
    }

    internal static async Task ReceiptAsync(NativeTextProtectedRebuildColdOwner owner,
        NativeTextProtectedRebuildColdOwner.Retained original, MutationReceipt[] mutations)
    {
        var receipt = original.Result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(original.Operation.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(owner.Source.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations.ToArray()).SequenceEqual(
            NativeSerialization.Serialize(mutations))).IsTrue();
    }

    internal static async Task UnchangedAsync(NativeTextProtectedRebuildColdOwner owner, string[] bytes, long cut)
    {
        await Assert.That(owner.Store.Position).IsEqualTo(cut);
        await Assert.That(NativeSerialization.Serialize(QueueWholeFlowStorage.Bytes(owner.Store))
            .SequenceEqual(NativeSerialization.Serialize(bytes))).IsTrue();
    }

    private static SearchRequest Request(NativeTextProtectedRebuildColdOwner owner, string term)
        => new(owner.Source.Partition, NativeTextProtectedRebuildColdSeed.Collection,
            NativeTextProtectedRebuildColdSeed.Field, term, Limit: 2);
}
