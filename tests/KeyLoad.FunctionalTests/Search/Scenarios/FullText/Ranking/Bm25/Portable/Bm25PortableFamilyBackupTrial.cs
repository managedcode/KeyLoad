using KeyLoad.FunctionalTests.BackupRestore;

namespace KeyLoad.FunctionalTests.Search;

internal static class Bm25PortableFamilyBackupTrial
{
    private const int FirstCold = 0;
    private const int ColdReopenCount = 2;

    internal static Task RunAsync(CancellationToken token) => EventingArtifactFixture.RunAsync(async fixture =>
    {
        var seeded = await Bm25PortableFamilyBackupSeed.CreateAsync(fixture, token);
        var receipts = seeded.Heads.Select(head => fixture.Source.ReadStoredOutcome(head.Generation)
            .Result.Get<CommitReceipt>()).ToArray();
        var sourceRows = Bm25PortableFamilyBytes.Complete(fixture.Source.Store, token);
        var sourcePosition = fixture.Source.Store.Position;
        var backup = Path.Combine(fixture.Root, "bm25-backup");
        var target = Path.Combine(fixture.Root, "bm25-target");
        var capture = await MixedEventingBackupRestore.CaptureAsync(fixture.Source, backup, token);
        await MixedEventingBackupRestore.RestoreAsync(fixture.Source,
            capture, backup, target, token);
        fixture.OpenTarget(target);
        var restoredPosition = fixture.Target!.Position;
        await Assert.That(restoredPosition).IsGreaterThan(0);
        await Assert.That(fixture.Target.Identity.Incarnation).IsNotEqualTo(seeded.Heads[0].Incarnation);
        await Assert.That(fixture.Target.Identity.DispatchPaused).IsTrue();
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target, seeded.Heads, seeded.Bytes, token);
        await RequireReceiptsAsync(fixture, receipts);
        var restored = Bm25PortableFamilyBytes.Complete(fixture.Target, token);
        for (var cold = FirstCold; cold < ColdReopenCount; cold++)
        {
            fixture.ReopenTarget(target);
            restoredPosition = await NativeStoreAssertions.ReopenedAsync(fixture.Target!, restoredPosition);
            await Assert.That(fixture.Target!.Position).IsEqualTo(restoredPosition);
            await Assert.That(fixture.Target.Identity.DispatchPaused).IsTrue();
            await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target, seeded.Heads, seeded.Bytes, token);
            await RequireReceiptsAsync(fixture, receipts);
            await Bm25PortableFamilyBytes.SameAsync(restored,
                Bm25PortableFamilyBytes.Complete(fixture.Target, token));
        }
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Bm25PortableFamilyBytes.SameAsync(sourceRows,
            Bm25PortableFamilyBytes.Complete(fixture.Source.Store, token));
    });

    private static async Task RequireReceiptsAsync(EventingArtifactFixture fixture, CommitReceipt[] expected)
    {
        foreach (var receipt in expected)
        {
            var actual = OutcomeStoreOracle.ReadPartition(fixture.Target!, fixture.Source.Partition,
                Bm25PortableFamilyBackupSeed.Root, receipt.CommandId)!.Value.Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt)))
                .IsTrue();
        }
    }
}
