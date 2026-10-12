using KeyLoad.Artifacts;
using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableFamilyBackupTrial
{
    internal static Task RunAsync(CancellationToken token) => EventingArtifactFixture.RunAsync(async fixture =>
    {
        var seeded = await Bm25PortableFamilyBackupSeed.CreateAsync(fixture, token);
        var repaired = await RefusalAndRepairAsync(fixture, seeded.Heads[0], seeded.Bytes[0], token);
        Bm25CorpusCurrentV1[] originals = [repaired.Head, seeded.Heads[1]];
        byte[][] originalBytes = [repaired.Bytes, seeded.Bytes[1]];
        var sourceRows = Bm25PortableFamilyBytes.Complete(fixture.Source.Store,
            fixture.Source.Database.EvaluationClock, token);
        var sourcePosition = fixture.Source.Store.Position;
        var backup = Path.Combine(fixture.Root, "bm25-backup");
        var archive = Path.Combine(fixture.Root, "bm25-archive");
        var unpacked = Path.Combine(fixture.Root, "bm25-unpacked");
        var target = Path.Combine(fixture.Root, "bm25-target");
        var capture = MixedEventingCatalogRestore.Capture(fixture.Source, backup, token);
        BackupArtifact.PackCatalogBackup(backup, archive, 4096, capture.ManifestDigest,
            fixture.Source.StorageExecution, fixture.Source.Database.EvaluationClock, token);
        BackupArtifact.UnpackCatalogBackup(archive, unpacked, capture.ManifestDigest,
            fixture.Source.StorageExecution, token);
        var restoredPosition = await MixedEventingCatalogRestore.RestoreAsync(fixture.Source,
            capture, unpacked, target, token);
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsEqualTo(restoredPosition);
        await Assert.That(fixture.Target.Identity.Incarnation).IsNotEqualTo(originals[0].Incarnation);
        await Assert.That(fixture.Target.Identity.DispatchPaused).IsTrue();
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target, originals, originalBytes,
            fixture.Database.EvaluationClock, token);
        var restored = Bm25PortableFamilyBytes.Complete(fixture.Target, fixture.Database.EvaluationClock, token);
        for (var cold = 0; cold < 2; cold++)
        {
            fixture.ReopenTarget(target);
            await Assert.That(fixture.Target!.Position).IsEqualTo(restoredPosition);
            await Assert.That(fixture.Target.Identity.DispatchPaused).IsTrue();
            await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target, originals, originalBytes,
                fixture.Database.EvaluationClock, token);
            await Bm25PortableFamilyBytes.SameAsync(restored,
                Bm25PortableFamilyBytes.Complete(fixture.Target, fixture.Database.EvaluationClock, token));
        }
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Bm25PortableFamilyBytes.SameAsync(sourceRows,
            Bm25PortableFamilyBytes.Complete(fixture.Source.Store, fixture.Source.Database.EvaluationClock, token));
    }, nativeAdmission: true);

    private static async Task<(Bm25CorpusCurrentV1 Head, byte[] Bytes)> RefusalAndRepairAsync(
        EventingArtifactFixture fixture, Bm25CorpusCurrentV1 current, byte[] original, CancellationToken token)
    {
        var key = Bm25CorpusKeys.Current(current);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            repairRequired = true;
            fixture.Source.Store.Commit((transaction, _) =>
            { transaction.Put(key, NativeSerialization.Serialize(current with { FormatVersion = 0 })); return true; });
            var before = Bm25PortableFamilyBytes.Complete(fixture.Source.Store,
                fixture.Source.Database.EvaluationClock, token);
            var position = fixture.Source.Store.Position;
            var journal = fixture.Source.ReadJournalCut();
            var rejected = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() =>
                MixedEventingCatalogRestore.Capture(fixture.Source,
                    Path.Combine(fixture.Root, "bm25-refused-backup"), token), token));
            await Assert.That(rejected!.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(fixture.Source.Store.Position).IsEqualTo(position);
            await Assert.That(fixture.Source.ReadJournalCut()).IsEqualTo(journal);
            await Bm25PortableFamilyBytes.SameAsync(before,
                Bm25PortableFamilyBytes.Complete(fixture.Source.Store, fixture.Source.Database.EvaluationClock, token));
        }, failures);
        if (repairRequired)
        {
            ServerFailureObserver.Observe(() => fixture.Source.Store.Commit((transaction, _) =>
            { transaction.Put(key, original); return true; }), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Source.Store, [current], [original],
            fixture.Source.Database.EvaluationClock, token);
        var published = await Bm25PortableFamilyBackupSeed.PublishAsync(fixture.Source,
            current.PrincipalId, token, current.Generation);
        await Assert.That(published.Head.Generation).IsNotEqualTo(current.Generation);
        await Assert.That(published.Head.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(published.Head.Statistics.TotalLength).IsEqualTo(2L);
        await Assert.That(published.Head.Statistics.Terms).IsEquivalentTo(new[] { "needle" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(published.Head.Statistics.DocumentFrequencies).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Source.Store,
            [published.Head], [published.Bytes], fixture.Source.Database.EvaluationClock, token);
        return published;
    }
}
