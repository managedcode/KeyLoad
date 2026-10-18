using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class SeriesPortableBackupTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupIndexedDocumentDedupFixture();
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupIndexedDocumentDedupFixture fixture, CancellationToken token)
    {
        var originals = await SeriesPortableBackupSeed.RunAsync(fixture, token);
        var source = BackupPersistedPolicyAssertions.Capture(fixture.Source.Store);
        var physical = SeriesPortableBackupAssertions.PhysicalRows(fixture.Source.Database, fixture.Partition);
        var outcomes = originals.Select(original => BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Partition, SeriesPortableBackupSeed.Root, original.Operation.CommandId)).ToArray();
        await SeriesPortableBackupAssertions.ReplayAsync(fixture.Source.Database, originals, token);
        await Assert.That(fixture.CreateBackup()).IsEqualTo(source.Position);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.BackupDirectory, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.BackupDirectory, UnitExecutionOptions.StorageExecution(), token);
        await BackupPersistedPolicyAssertions.SameAsync(fixture.Source.Store.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        var restore = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.BackupDirectory, fixture.TargetDirectory], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore);
        var database = fixture.OpenRestored();
        await Assert.That(database.Store.Identity.Incarnation).IsNotEqualTo(fixture.Source.Store.Identity.Incarnation);
        await Assert.That(database.Store.Identity.NodeId).IsNotEqualTo(fixture.Source.Store.Identity.NodeId);
        await Assert.That(database.Store.Identity.DispatchPaused).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(source.Position + 1L);
        await Assert.That(SeriesPortableBackupAssertions.PhysicalRows(database, fixture.Partition))
            .IsEquivalentTo(physical, CollectionOrdering.Matching);
        await SeriesPortableBackupAssertions.SourceOutcomesAsync(fixture, outcomes, originals);
        var restored = BackupPersistedPolicyAssertions.Capture(database.Store);
        var fenced = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(originals[0].Operation));
        await Assert.That(fenced.Code).IsEqualTo(ErrorCode.Corruption);
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, restored);
        await SeriesPortableBackupAssertions.VisibleAsync(database, fixture.Partition, false, token);
        var target = await ContinueAsync(fixture, database, token);
        await ColdAsync(fixture, database, originals, outcomes, target, token);
        await BackupPersistedPolicyAssertions.UnchangedAsync(fixture.Source.Store, source);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.BackupDirectory, token);
    }

    private static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)[]> ContinueAsync(
        BackupIndexedDocumentDedupFixture fixture, DatabaseEngine database, CancellationToken token)
    {
        var physical = SeriesPortableBackupAssertions.PhysicalRows(database, fixture.Partition);
        var refused = SeriesPortableBackupSeed.Batch(database, fixture.Partition,
            new AppendSamples(SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series,
                [SeriesPortableBackupSeed.Data(6, 4, 6), SeriesPortableBackupSeed.Data(7, 1, 7)]));
        var refusal = database.ApplyEmbedded(refused, token);
        await Assert.That(Errors.Code(refusal.Problem)).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(SeriesPortableBackupAssertions.PhysicalRows(database, fixture.Partition))
            .IsEquivalentTo(physical, CollectionOrdering.Matching);
        await SeriesPortableBackupAssertions.VisibleAsync(database, fixture.Partition, false, token);
        await BackupPersistedPolicyAssertions.ReplayAsync(database, refused, refusal, token);
        var expired = SeriesPortableBackupSeed.Batch(database, fixture.Partition,
            new ExpireSamples(SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series, SeriesPortableBackupSeed.Floor, 1));
        var expiration = database.ApplyEmbedded(expired, token);
        await SeriesPortableBackupAssertions.ReceiptAsync(database, fixture.Partition, expired, expiration);
        var appended = SeriesPortableBackupSeed.Batch(database, fixture.Partition,
            new AppendSamples(SeriesPortableBackupSeed.Set, SeriesPortableBackupSeed.Series,
                [SeriesPortableBackupSeed.Data(5, 4, 5)]));
        var append = database.ApplyEmbedded(appended, token);
        await SeriesPortableBackupAssertions.ReceiptAsync(database, fixture.Partition, appended, append);
        await SeriesPortableBackupAssertions.VisibleAsync(database, fixture.Partition, true, token);
        return [(refused, refusal), (expired, expiration), (appended, append)];
    }

    private static async Task ColdAsync(BackupIndexedDocumentDedupFixture fixture, DatabaseEngine database,
        (Command<DatabaseCommand> Operation, Result<object> Result)[] source, byte[][] sourceOutcomes,
        (Command<DatabaseCommand> Operation, Result<object> Result)[] target, CancellationToken token)
    {
        await SeriesPortableBackupAssertions.ReplayAsync(database, target, token);
        var identity = database.Store.Identity;
        var final = BackupPersistedPolicyAssertions.Capture(database.Store);
        for (var index = 0; index < 2; index++)
        {
            fixture.CloseRestored();
            database = fixture.OpenRestored();
            await BackupPersistedPolicyAssertions.SameAsync(identity, database.Store.Identity);
            await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, final);
            await SeriesPortableBackupAssertions.VisibleAsync(database, fixture.Partition, true, token);
            await SeriesPortableBackupAssertions.SourceOutcomesAsync(fixture, sourceOutcomes, source);
            await SeriesPortableBackupAssertions.ReplayAsync(database, target, token);
            await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, final);
        }
    }
}
