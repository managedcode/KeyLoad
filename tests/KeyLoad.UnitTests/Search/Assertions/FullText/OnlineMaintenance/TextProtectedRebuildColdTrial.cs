using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextProtectedRebuildColdTrial
{
    internal static async Task RunAsync(TextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var originals = await WarmAsync(owner, token);
        TextProtectedRebuildColdSeed.Policy(owner, 2L, allowed: false);
        await TextProtectedRebuildColdAssertions.DeniedAsync(owner, token);
        var beforeCold = QueueWholeFlowStorage.Bytes(owner.Store);
        var coldCut = owner.Store.Position;
        owner.Reopen(removeProjection: true);
        await TextProtectedRebuildColdAssertions.UnchangedAsync(owner, beforeCold, coldCut);
        await TextProtectedRebuildColdAssertions.DeniedAsync(owner, token);
        TextProtectedRebuildColdSeed.Policy(owner, 3L, allowed: true);
        await TextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        foreach (var original in originals)
        {
            await TextProtectedRebuildColdAssertions.ReplayAsync(owner, original, token);
        }
        var repaired = QueueWholeFlowStorage.Bytes(owner.Store);
        var repairedCut = owner.Store.Position;
        owner.Reopen(removeProjection: false);
        await TextProtectedRebuildColdAssertions.UnchangedAsync(owner, repaired, repairedCut);
        await TextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        foreach (var original in originals)
        {
            await TextProtectedRebuildColdAssertions.ReplayAsync(owner, original, token);
        }
        var healthy = TextProtectedRebuildColdSeed.Batch(owner,
            new PutDocument(TextProtectedRebuildColdSeed.Collection, "gamma",
                TextProtectedRebuildColdSeed.HealthyJson, Access: new(TextProtectedRebuildColdSeed.Reader)));
        await TextProtectedRebuildColdAssertions.ReceiptAsync(owner, healthy,
            [new("putDocument", TextProtectedRebuildColdSeed.Collection, "gamma", 1L)]);
        await TextProtectedRebuildColdAssertions.LiteralAsync(owner, "завершено", "gamma", 1L,
            "{\"other\":\"gamma\"}", token);
        await TextProtectedRebuildColdAssertions.ReplayAsync(owner, healthy, token);
        await TextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
    }

    private static async Task<TextProtectedRebuildColdOwner.Retained[]> WarmAsync(
        TextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var seed = TextProtectedRebuildColdSeed.Initialize(owner)[0];
        await TextProtectedRebuildColdAssertions.ReceiptAsync(owner, seed,
            [new("putDocument", TextProtectedRebuildColdSeed.Collection, "alpha", 1L),
             new("putDocument", TextProtectedRebuildColdSeed.Collection, "beta", 1L)]);
        await TextProtectedRebuildColdAssertions.LiteralAsync(owner, "ПРИВІТ", "alpha", 1L,
            "{\"other\":\"alpha\"}", token);
        await TextProtectedRebuildColdAssertions.LiteralAsync(owner, "hello", "beta", 1L,
            "{\"other\":\"beta\"}", token);
        var patch = TextProtectedRebuildColdSeed.Batch(owner,
            new PatchDocument(TextProtectedRebuildColdSeed.Collection, "alpha",
                [new("/secret", PatchKind.Set, "\"оновлено світ\"")], 1L));
        var deleted = TextProtectedRebuildColdSeed.Batch(owner,
            new DeleteDocument(TextProtectedRebuildColdSeed.Collection, "beta", 1L));
        await TextProtectedRebuildColdAssertions.ReceiptAsync(owner, patch,
            [new("patchDocument", TextProtectedRebuildColdSeed.Collection, "alpha", 2L)]);
        await TextProtectedRebuildColdAssertions.ReceiptAsync(owner, deleted,
            [new("deleteDocument", TextProtectedRebuildColdSeed.Collection, "beta", 2L)]);
        await TextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        return [seed, patch, deleted];
    }
}
