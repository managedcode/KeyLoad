using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class CliBackupJournalRepairTrial
{
    internal static async Task RunAsync(bool checkpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(options, fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, CancellationToken token)
    {
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        await RefuseAndRepairAsync(options, fixture, archive, source, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        var restored = await CliBackupRestoreProcess.RunAsync(options,
            ["restore", fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restored);
        var cut = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(restored, cut.Identity);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, cut.Identity, cut);
        await BackupJournalRepairAssertions.SameCutAsync(cut,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(cut,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAndRepairAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, Dictionary<string, byte[]> archive,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        var original = archive[MetadataTestContract.JournalFileName];
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var damaged = original.ToArray();
            damaged[BackupCutFixture.LastFrameOffset(damaged)
                + ZoneTreePersistenceFormat.ChecksumOffset] ^= 1;
            repairRequired = true;
            await fixture.RewriteJournalAsync(damaged);
            var faulted = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            await RefusalAsync(options, fixture, faulted, source, token);
            await RefusalAsync(options, fixture, faulted, source, token);
        }, failures);
        if (repairRequired)
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
                fixture.Journal, original, CancellationToken.None), failures);
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
                Path.Combine(fixture.Backup, MetadataTestContract.ManifestFileName),
                archive[MetadataTestContract.ManifestFileName], CancellationToken.None), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusalAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, Dictionary<string, byte[]> faulted,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        var rejected = await CliBackupRestoreProcess.RunAsync(options,
            ["restore", fixture.Backup, fixture.Destination], token);
        await CliBackupJournalRepairAssertions.RefusedAsync(rejected);
        await Assert.That(Directory.Exists(fixture.Destination) || File.Exists(fixture.Destination)).IsFalse();
        await BackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
    }
}
