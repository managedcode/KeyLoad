using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BackupIndexedDocumentDedupCases
{
    private const string RootPrincipal = "root";
    private const int AuthorityResetCommits = 1;
    private const string SystemNamespace = ZoneTreePersistenceFormat.SystemNamespace;
    private const string DispatchPausedKey = ZoneTreePersistenceFormat.DispatchPausedKey;
    private static readonly Guid SeedCommandId = Guid.Parse("fbeb87ad-22a1-4bce-86c0-74948a9a7da0");
    private static readonly Guid ReplaceCommandId = Guid.Parse("448a6eb4-46b0-4f2e-9d40-f6cb1f8b64a0");
    private static readonly Guid DeleteCommandId = Guid.Parse("16f08c15-51a3-4420-a8cc-4a09c86fded6");
    private static readonly Guid ConflictCommandId = Guid.Parse("2d8ed8cb-b757-4874-8913-99840a2aaada");
    private static readonly Guid RestoredCommandId = Guid.Parse("725d0b6b-1ae2-47d0-a6e8-befab8f5dfdc");
    private static readonly Guid HealthyCommandId = Guid.Parse("9fc054f3-913e-4208-9b53-cb10415b9081");

    [Test]
    public async Task AcBackupContent001CurrentIndexedDocumentsRestoreWithIncarnationFencedDedup()
    {
        using var fixture = new BackupIndexedDocumentDedupFixture();
        var firstOperation = await SeedAndMutate(fixture);
        await BackupIndexedDocumentDedupAssertions.AssertPreRestoreContent(fixture.Source.Database, fixture.Partition);
        var sourceIdentity = fixture.Source.Store.Identity;
        var sourcePhysicalShardId = fixture.Source.Database.ReadPhysicalShardCatalog(RootPrincipal)
            .DefaultShard.PhysicalShardId;
        var sourceCut = fixture.CreateBackup();
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.BackupDirectory);
        var archive = await fixture.CaptureArchiveAsync();
        var originalOutcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Partition, RootPrincipal, SeedCommandId);
        await Assert.That(manifest.Position).IsEqualTo(sourceCut);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourceCut);
        var restoredIdentity = fixture.Restore();
        var database = fixture.OpenRestored();
        await AssertRestoreAuthority(fixture, restoredIdentity, sourceIdentity, sourcePhysicalShardId,
            sourceCut, manifest.Position);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(fixture.TargetStore,
            fixture.Partition, SeedCommandId, originalOutcome);
        await AssertPreRestoreReplayFence(fixture, database, firstOperation, sourceCut + AuthorityResetCommits,
            originalOutcome);
        await BackupIndexedDocumentDedupAssertions.AssertPreRestoreContent(database, fixture.Partition);
        await ReplayPostRestoreAndWriteHealthy(fixture, database);
        var finalPosition = fixture.TargetStore.Position;
        var finalOutcomes = BackupIndexedDocumentDedupAssertions.CaptureCurrentOutcomes(fixture.TargetStore,
            fixture.Partition, RestoredCommandId, HealthyCommandId);
        fixture.CloseRestored();
        var reopened = fixture.OpenRestored();
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(finalPosition);
        await BackupIndexedDocumentDedupAssertions.AssertFinalContent(reopened, fixture.Partition);
        await BackupIndexedDocumentDedupAssertions.AssertCurrentOutcomes(reopened.Store, fixture.Partition, finalOutcomes);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(fixture.BackupDirectory, archive);
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourceCut);
    }

    private static async Task<ReplicatedOperation> SeedAndMutate(BackupIndexedDocumentDedupFixture fixture)
    {
        fixture.Source.Configure(BackupIndexedDocumentDedupFixture.Collection, ResourceKind.Collection,
            indexes: [new(BackupIndexedDocumentDedupFixture.IndexName,
                [BackupIndexedDocumentDedupFixture.LabelPath], Unique: true)]);
        var seed = fixture.Batch(fixture.Source.Database, SeedCommandId,
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.FirstId,
                BackupIndexedDocumentDedupFixture.FirstJson, BackupIndexedDocumentDedupFixture.NoPriorRevision),
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.SecondId,
                BackupIndexedDocumentDedupFixture.SecondJson, BackupIndexedDocumentDedupFixture.NoPriorRevision),
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.DeletedId,
                BackupIndexedDocumentDedupFixture.DeletedJson, BackupIndexedDocumentDedupFixture.NoPriorRevision));
        BackupIndexedDocumentDedupFixture.RequireSuccess(fixture.Source.Database.Apply(seed));
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.AlphaLabel, [BackupIndexedDocumentDedupFixture.FirstId]);
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.CobaltLabel, [BackupIndexedDocumentDedupFixture.SecondId]);
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.JadeLabel, [BackupIndexedDocumentDedupFixture.DeletedId]);
        var replace = fixture.Batch(fixture.Source.Database, ReplaceCommandId,
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.FirstId,
                BackupIndexedDocumentDedupFixture.ReplacedJson, BackupIndexedDocumentDedupFixture.FirstRevision,
                ExplicitReplacement: true));
        BackupIndexedDocumentDedupFixture.RequireSuccess(fixture.Source.Database.Apply(replace));
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.AlphaLabel, []);
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.VioletLabel, [BackupIndexedDocumentDedupFixture.FirstId]);
        var delete = fixture.Batch(fixture.Source.Database, DeleteCommandId,
            new DeleteDocument(BackupIndexedDocumentDedupFixture.Collection,
                BackupIndexedDocumentDedupFixture.DeletedId, BackupIndexedDocumentDedupFixture.FirstRevision));
        BackupIndexedDocumentDedupFixture.RequireSuccess(fixture.Source.Database.Apply(delete));
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(fixture.Source.Database, fixture.Partition,
            BackupIndexedDocumentDedupFixture.JadeLabel, []);
        var conflict = fixture.Batch(fixture.Source.Database, ConflictCommandId,
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.TransientId,
                BackupIndexedDocumentDedupFixture.TemporaryConflictJson, BackupIndexedDocumentDedupFixture.NoPriorRevision),
            new PatchDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.SecondId,
                [new(BackupIndexedDocumentDedupFixture.LabelPath, PatchKind.Set, BackupIndexedDocumentDedupFixture.ReplacedLabelJson)],
                BackupIndexedDocumentDedupFixture.FirstRevision));
        var rejected = fixture.Source.Database.Apply(conflict);
        if (rejected.Error != ErrorCode.Conflict)
        {
            throw new InvalidOperationException("The real unique-index conflict batch was not rejected.");
        }
        BackupIndexedDocumentDedupFixture.RequireSuccess(fixture.SeedReplicaAuthority());
        fixture.AssertSourceAuthority();
        return seed;
    }

    private static async Task AssertRestoreAuthority(BackupIndexedDocumentDedupFixture fixture,
        StoreIdentity restored, StoreIdentity source, Guid physicalShardId, long sourceCut, long manifestPosition)
    {
        await Assert.That(fixture.TargetStore.Identity.NodeId).IsEqualTo(restored.NodeId);
        await Assert.That(fixture.TargetStore.Identity.Incarnation).IsEqualTo(restored.Incarnation);
        await Assert.That(fixture.TargetStore.Identity.SigningKey.Span.SequenceEqual(restored.SigningKey.Span)).IsTrue();
        await Assert.That(restored.NodeId).IsNotEqualTo(source.NodeId);
        await Assert.That(restored.Incarnation).IsNotEqualTo(source.Incarnation);
        await Assert.That(restored.DispatchPaused).IsTrue();
        var catalog = fixture.RestoredDatabase.ReadPhysicalShardCatalog(RootPrincipal);
        await Assert.That(catalog.DefaultShard.PhysicalShardId).IsEqualTo(physicalShardId);
        await Assert.That(catalog.DefaultShard.Incarnation).IsEqualTo(source.Incarnation);
        await Assert.That(manifestPosition).IsEqualTo(sourceCut);
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(sourceCut + AuthorityResetCommits);
        await Assert.That(ReadSystem(fixture.TargetStore, ZoneTreePersistenceFormat.LastAppliedKey)).IsNull();
        await Assert.That(ReadSystem(fixture.TargetStore, ZoneTreePersistenceFormat.ClockKey)).IsNull();
        await Assert.That(fixture.TargetStore.Read(view => view.ReadOwnedValue(KeyCodec.Encode(
            ZoneTreePersistenceFormat.MembershipNamespace, ZoneTreePersistenceFormat.OrleansMembershipKey)))).IsNull();
        var paused = ReadSystem(fixture.TargetStore, DispatchPausedKey);
        await Assert.That(paused).IsNotNull();
        await Assert.That(NativeSerialization.Deserialize<bool>(paused!)).IsTrue();
    }

    private static byte[]? ReadSystem(ZoneTreeStore store, string key)
        => store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(SystemNamespace, key)));

    private static async Task AssertPreRestoreReplayFence(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database, ReplicatedOperation original, long expectedPosition, byte[] originalOutcome)
    {
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(expectedPosition);
        var rejected = database.Apply(original);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(expectedPosition);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(fixture.TargetStore,
            fixture.Partition, SeedCommandId, originalOutcome);
    }

    private static async Task ReplayPostRestoreAndWriteHealthy(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database)
    {
        var restored = fixture.Batch(database, RestoredCommandId,
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.RestoredId,
                BackupIndexedDocumentDedupFixture.RestoredJson, BackupIndexedDocumentDedupFixture.NoPriorRevision));
        var firstResult = database.Apply(restored);
        await Assert.That(firstResult.Error).IsNull();
        var firstBytes = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.TargetStore,
            fixture.Partition, RootPrincipal, RestoredCommandId);
        var position = fixture.TargetStore.Position;
        var replay = database.Apply(restored);
        await Assert.That(replay.Error).IsNull();
        await NativeReplayResultAssertions.Same<CommitReceipt>(replay, firstResult);
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(position);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(fixture.TargetStore,
            fixture.Partition, RestoredCommandId, firstBytes);
        await BackupIndexedDocumentDedupAssertions.AssertIndexed(database, fixture.Partition, BackupIndexedDocumentDedupFixture.AmberLabel,
            [BackupIndexedDocumentDedupFixture.RestoredId]);
        var healthy = fixture.Batch(database, HealthyCommandId,
            new PutDocument(BackupIndexedDocumentDedupFixture.Collection, BackupIndexedDocumentDedupFixture.HealthyId,
                BackupIndexedDocumentDedupFixture.HealthyJson, BackupIndexedDocumentDedupFixture.NoPriorRevision));
        await Assert.That(database.Apply(healthy).Error).IsNull();
    }
}
