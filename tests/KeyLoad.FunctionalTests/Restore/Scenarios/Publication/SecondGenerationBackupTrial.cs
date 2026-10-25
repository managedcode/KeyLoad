using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class SecondGenerationBackupTrial
{
    private const string SecondArchiveName = "second-archive";
    private const string SecondTargetName = "second-target";
    private const string RestoreCommand = "restore";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var restored = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        var parent = Path.GetDirectoryName(fixture.Destination)!;
        var archive = Path.Combine(parent, SecondArchiveName);
        var target = Path.Combine(parent, SecondTargetName);
        await CreateSecondAsync(fixture.Destination, archive, token);
        var current = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await SecondGenerationBackupAssertions.LiteralAsync(current);
        var captured = await BackupJournalRepairAssertions.ArchiveAsync(archive, token);
        await SecondGenerationBackupAssertions.CheckpointAsync(original, captured, source.Identity, current.Identity);
        var verified = ZoneTreeStore.VerifyBackup(archive, UnitExecutionOptions.StorageExecution(), token);
        await BackupPersistedPolicyAssertions.SameAsync(current.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(current.Position);
        await SecondGenerationBackupAssertions.ConflictAsync(archive, fixture.Destination, current, captured, token);
        await RestoreAndColdAsync(archive, target, current, token);
        await BackupJournalRepairAssertions.SameCutAsync(current,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(captured, archive, token);
    }

    private static async Task CreateSecondAsync(string current, string archive, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(current), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                var before = store.Position;
                store.Commit((tx, _) =>
                {
                    tx.PutRecord(MetadataBackupFixture.StoredKeyBytes, SecondGenerationBackupAssertions.CurrentValue);
                    return true;
                });
                await Assert.That(store.Position).IsEqualTo(before + 1L);
                await Assert.That(store.CreateBackup(archive)).IsEqualTo(store.Position);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RestoreAndColdAsync(string archive, string target,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) current, CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [RestoreCommand, archive, target], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var actual = BackupJournalRepairAssertions.Read(target, token);
        await SecondGenerationBackupAssertions.RestoredAsync(current, actual);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, actual.Identity);
        await BackupJournalRepairAssertions.SameCutAsync(actual,
            BackupJournalRepairAssertions.Read(target, token));
        await BackupJournalRepairAssertions.SameCutAsync(actual,
            BackupJournalRepairAssertions.Read(target, token));
    }
}
