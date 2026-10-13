using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProtectedRebuildColdTrial
{
    internal static async Task RunAsync(NativeTextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var originals = await WarmAsync(owner, token);
        NativeTextProtectedRebuildColdSeed.Policy(owner, 2L, allowed: false);
        await NativeTextProtectedRebuildColdAssertions.DeniedAsync(owner, token);
        var beforeCold = QueueWholeFlowStorage.Bytes(owner.Store);
        var coldCut = owner.Store.Position;
        owner.Reopen(removeProjection: true);
        await NativeTextProtectedRebuildColdAssertions.UnchangedAsync(owner, beforeCold, coldCut);
        await NativeTextProtectedRebuildColdAssertions.DeniedAsync(owner, token);
        NativeTextProtectedRebuildColdSeed.Policy(owner, 3L, allowed: true);
        await NativeTextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        foreach (var original in originals)
        { await NativeTextProtectedRebuildColdAssertions.ReplayAsync(owner, original, token); }
        var repaired = QueueWholeFlowStorage.Bytes(owner.Store);
        var repairedCut = owner.Store.Position;
        owner.Reopen(removeProjection: false);
        await NativeTextProtectedRebuildColdAssertions.UnchangedAsync(owner, repaired, repairedCut);
        await NativeTextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        foreach (var original in originals)
        { await NativeTextProtectedRebuildColdAssertions.ReplayAsync(owner, original, token); }
        var healthy = NativeTextProtectedRebuildColdSeed.Batch(owner,
            new PutDocument(NativeTextProtectedRebuildColdSeed.Collection, "gamma",
                NativeTextProtectedRebuildColdSeed.HealthyJson, Access: new(NativeTextProtectedRebuildColdSeed.Reader)));
        await NativeTextProtectedRebuildColdAssertions.ReceiptAsync(owner, healthy,
            [new("putDocument", NativeTextProtectedRebuildColdSeed.Collection, "gamma", 1L)]);
        await NativeTextProtectedRebuildColdAssertions.LiteralAsync(owner, "завершено", "gamma", 1L,
            "{\"other\":\"gamma\"}", token);
        await NativeTextProtectedRebuildColdAssertions.ReplayAsync(owner, healthy, token);
        await NativeTextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
    }

    private static async Task<NativeTextProtectedRebuildColdOwner.Retained[]> WarmAsync(
        NativeTextProtectedRebuildColdOwner owner, CancellationToken token)
    {
        var seed = NativeTextProtectedRebuildColdSeed.Initialize(owner)[0];
        await NativeTextProtectedRebuildColdAssertions.ReceiptAsync(owner, seed,
            [new("putDocument", NativeTextProtectedRebuildColdSeed.Collection, "alpha", 1L),
             new("putDocument", NativeTextProtectedRebuildColdSeed.Collection, "beta", 1L)]);
        await NativeTextProtectedRebuildColdAssertions.LiteralAsync(owner, "ПРИВІТ", "alpha", 1L,
            "{\"other\":\"alpha\"}", token);
        await NativeTextProtectedRebuildColdAssertions.LiteralAsync(owner, "hello", "beta", 1L,
            "{\"other\":\"beta\"}", token);
        var patch = NativeTextProtectedRebuildColdSeed.Batch(owner,
            new PatchDocument(NativeTextProtectedRebuildColdSeed.Collection, "alpha",
                [new("/secret", PatchKind.Set, "\"оновлено світ\"")], 1L));
        var deleted = NativeTextProtectedRebuildColdSeed.Batch(owner,
            new DeleteDocument(NativeTextProtectedRebuildColdSeed.Collection, "beta", 1L));
        await NativeTextProtectedRebuildColdAssertions.ReceiptAsync(owner, patch,
            [new("patchDocument", NativeTextProtectedRebuildColdSeed.Collection, "alpha", 2L)]);
        await NativeTextProtectedRebuildColdAssertions.ReceiptAsync(owner, deleted,
            [new("deleteDocument", NativeTextProtectedRebuildColdSeed.Collection, "beta", 2L)]);
        await NativeTextProtectedRebuildColdAssertions.CurrentAsync(owner, token);
        return [seed, patch, deleted];
    }
}
