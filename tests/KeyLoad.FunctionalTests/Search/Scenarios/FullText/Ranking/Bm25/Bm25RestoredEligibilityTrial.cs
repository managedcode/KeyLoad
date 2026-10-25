using KeyLoad.FunctionalTests.BackupRestore;
using KeyLoad.Storage.ZoneTree.Restore;

namespace KeyLoad.FunctionalTests.Search;

internal static class Bm25RestoredEligibilityTrial
{
    private const int ArtifactPieceBytes = 4_096;
    private const int FirstCold = 0;
    private const int ColdReopenCount = 2;

    internal static Task RunAsync(CancellationToken token) => EventingArtifactFixture.RunAsync(async fixture =>
    {
        var seeded = await Bm25PortableFamilyBackupSeed.CreateAsync(fixture, token);
        var original = fixture.Source.ReadStoredOutcome(seeded.Heads[0].Generation).PreparedCommand!;
        var receipt = OutcomeStoreOracle.ReadPartition(fixture.Source.Store, fixture.Source.Partition,
            Bm25PortableFamilyBackupSeed.Root, original.CommandId)!.Value.Get<CommitReceipt>();
        var source = Bm25RestoredEligibilityAssertions.Cut(fixture.Source.Store, fixture.Source.Database,
            fixture.Source.ReadCanonicalCut(), token);
        var targetPath = await RestoreAsync(fixture, token);
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target!, seeded.Heads, seeded.Bytes,
            fixture.Database.EvaluationClock, token);
        await Assert.That(fixture.Target!.Identity.Incarnation).IsNotEqualTo(seeded.Heads[0].Incarnation);
        await Assert.That(fixture.Target.Identity.DispatchPaused).IsTrue();
        await RequireSearchAsync(fixture, false, token);
        var repaired = await Bm25RestoredEligibilityPolicy.RepairAsync(fixture, seeded.Heads[0], token);
        await Assert.That(repaired.Head.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(repaired.Head.Statistics.TotalLength).IsEqualTo(2L);
        await Assert.That(repaired.Head.Statistics.DocumentFrequencies).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Bm25RestoredEligibilityAssertions.ReplayAsync(fixture, true, original, receipt, token);
        var targetNode = fixture.Target.Identity.NodeId;
        var targetIncarnation = fixture.Target.Identity.Incarnation;
        var targetRows = Bm25PortableFamilyBytes.Complete(fixture.Target, fixture.Database.EvaluationClock, token);
        var targetPosition = fixture.Target.Position;
        var targetCut = fixture.ReadTargetCut();
        for (var cold = FirstCold; cold < ColdReopenCount; cold++)
        {
            fixture.ReopenTarget(targetPath);
            await Assert.That(fixture.Target!.Identity.NodeId).IsEqualTo(targetNode);
            await Assert.That(fixture.Target.Identity.Incarnation).IsEqualTo(targetIncarnation);
            await Assert.That(fixture.Target.Position).IsEqualTo(targetPosition);
            await Assert.That(fixture.ReadTargetCut()).IsEqualTo(targetCut);
            await Bm25PortableFamilyBytes.SameAsync(targetRows,
                Bm25PortableFamilyBytes.Complete(fixture.Target, fixture.Database.EvaluationClock, token));
            await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Target,
                [repaired.Head, seeded.Heads[1]], [repaired.Bytes, seeded.Bytes[1]], fixture.Database.EvaluationClock, token);
            await RequireSearchAsync(fixture, true, token);
            await Bm25RestoredEligibilityAssertions.ReplayAsync(fixture, false, repaired.Operation, repaired.Receipt, token);
        }
        await Bm25RestoredEligibilityAssertions.SameAsync(source,
            Bm25RestoredEligibilityAssertions.Cut(fixture.Source.Store, fixture.Source.Database, fixture.Source.ReadCanonicalCut(), token));
        await Bm25PortableFamilyBytes.RequireHeadsAsync(fixture.Source.Store, seeded.Heads, seeded.Bytes,
            fixture.Source.Database.EvaluationClock, token);
    });

    private static async Task<string> RestoreAsync(EventingArtifactFixture fixture, CancellationToken token)
    {
        var backup = Path.Combine(fixture.Root, "restored-eligibility-backup");
        var archive = Path.Combine(fixture.Root, "restored-eligibility-archive");
        var unpacked = Path.Combine(fixture.Root, "restored-eligibility-unpacked");
        var target = Path.Combine(fixture.Root, "restored-eligibility-target");
        var capture = MixedEventingBackupRestore.Capture(fixture.Source, backup, token);
        BackupArtifact.Pack(backup, archive, ArtifactPieceBytes, fixture.Source.Database.EvaluationClock);
        BackupArtifact.Unpack(archive, unpacked);
        var position = await MixedEventingBackupRestore.RestoreAsync(fixture.Source, capture, unpacked, target, token);
        fixture.OpenTarget(target);
        await Assert.That(fixture.Target!.Position).IsEqualTo(position);
        return target;
    }

    private static async Task RequireSearchAsync(EventingArtifactFixture fixture, bool repaired, CancellationToken token)
    {
        var before = Bm25RestoredEligibilityAssertions.Cut(fixture.Target!, fixture.Database,
            fixture.ReadTargetCut(), token);
        foreach (var subject in new[] { Bm25PortableFamilyBackupSeed.FirstSubject, Bm25PortableFamilyBackupSeed.SecondSubject })
        {
            var current = Bm25RestoredEligibilityAssertions.Current(fixture, subject);
            if (repaired && subject == Bm25PortableFamilyBackupSeed.FirstSubject)
            {
                await Assert.That(current!.DocumentCount).IsEqualTo(1);
            }
            else
            {
                await Assert.That(current).IsNull();
            }
            var source = await Bm25RestoredEligibilityAssertions.SearchAsync(fixture.Source.Database,
                fixture.Source.Partition, subject, token);
            var target = await Bm25RestoredEligibilityAssertions.SearchAsync(fixture.Database,
                fixture.Source.Partition, subject, token);
            await Assert.That(JsonDefaults.Serialize(target).AsSpan().SequenceEqual(JsonDefaults.Serialize(source))).IsTrue();
        }
        await Bm25RestoredEligibilityAssertions.SameAsync(before,
            Bm25RestoredEligibilityAssertions.Cut(fixture.Target!, fixture.Database, fixture.ReadTargetCut(), token));
    }
}
