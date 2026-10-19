using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CheckpointFooterRepairTrial
{
    private const string ErrorCodeKey = "errorCode";
    private const string DetailKey = "detail";
    private const string RestoreCommand = "restore";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint: true);
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
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var restored = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, restored.Identity);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAndRepairAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, Dictionary<string, byte[]> archive,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var damaged = CheckpointFooterRepairFault.Create(archive[MetadataTestContract.JournalFileName]);
            repairRequired = true;
            await fixture.RewriteJournalAsync(damaged);
            var faulted = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            await RefusedAsync(options, fixture, faulted, source, token);
            await RefusedAsync(options, fixture, faulted, source, token);
        }, failures);
        if (repairRequired)
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(fixture.Journal,
                archive[MetadataTestContract.JournalFileName], CancellationToken.None), failures);
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
                Path.Combine(fixture.Backup, MetadataTestContract.ManifestFileName),
                archive[MetadataTestContract.ManifestFileName], CancellationToken.None), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusedAsync(IOptions<TestExecutionOptions> options,
        BackupCutFixture fixture, Dictionary<string, byte[]> faulted,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(
            fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeKey).GetString()).IsEqualTo(ErrorCode.Corruption.ToString());
        await Assert.That(problem.RootElement.GetProperty(DetailKey).GetString())
            .IsEqualTo(ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        await Assert.That(Directory.Exists(fixture.Destination) || File.Exists(fixture.Destination)).IsFalse();
        await BackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
    }
}
