using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupCreateSourceAccessFlow
{
    private const string CandidateSuffix = "-source-access";
    private const string JournalName = "commands.wal";
    private const string RestoreCommand = "restore";

    internal static async Task RunAsync(CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(options, fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        BackupCutFixture fixture, CancellationToken token)
    {
        var original = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var candidate = fixture.Backup + CandidateSuffix;
        var rejectedDestination = fixture.Destination + CandidateSuffix;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var region = new BackupSourcePermissionRegion(Path.Combine(fixture.Source, JournalName));
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    region.DenyNewReaders();
                    _ = Assert.ThrowsExactly<UnauthorizedAccessException>(() => store.CreateBackup(candidate));
                    await BackupCreateSourceAccessAssertions.UnpublishedAsync(candidate);
                    await BackupCreateSourceAccessAssertions.OwnedCutAsync(store, original, token);
                    await region.RequireOriginalAsync(archive[JournalName], token);
                    var failedOutput = await CliBackupRestoreAssertions.CaptureFilesAsync(candidate, token);
                    var restore = await CliBackupRestoreProcess.RunAsync(options,
                        [RestoreCommand, candidate, rejectedDestination], token);
                    await CliBackupRestoreAssertions.RejectedProcessAsync(restore);
                    await Assert.That(restore.StandardOutput).IsEqualTo(string.Empty);
                    await Assert.That(Directory.Exists(rejectedDestination)).IsFalse();
                    await CliBackupRestoreAssertions.FilesEqualAsync(failedOutput, candidate, token);
                    await BackupCreateSourceAccessAssertions.OwnedCutAsync(store, original, token);
                    await region.RequireOriginalAsync(archive[JournalName], token);
                }, failures);
                ServerFailureObserver.Observe(region.Repair, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await BackupJournalRepairAssertions.SameCutAsync(original,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupCreateSourceAccessAssertions.ReconcileOwnedPartialAsync(candidate);
        await CaptureAsync(fixture, candidate, original.Position, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(archive, fixture.Backup, token);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, candidate, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        await BackupCreateSourceAccessAssertions.RestoredAsync(fixture, result, token);
        var restored = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupCreateSourceAccessAssertions.RestoredAsync(fixture, result, token);
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupCreateSourceAccessAssertions.RestoredAsync(fixture, result, token);
        await BackupJournalRepairAssertions.SameCutAsync(original,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await CliBackupRestoreAssertions.FilesEqualAsync(archive, fixture.Backup, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(archive, candidate, token);
    }

    private static async Task CaptureAsync(BackupCutFixture fixture, string candidate, long originalPosition,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                await Assert.That(store.CreateBackup(candidate)).IsEqualTo(originalPosition);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
