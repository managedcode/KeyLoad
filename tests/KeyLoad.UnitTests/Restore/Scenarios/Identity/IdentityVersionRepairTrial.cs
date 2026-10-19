using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class IdentityVersionRepairTrial
{
    private const string ErrorCodeKey = "errorCode";
    private const string DetailKey = "detail";

    internal static async Task RunAsync(bool codecVersion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture();
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, codecVersion, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, bool codecVersion, CancellationToken token)
    {
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var identityPath = Path.Combine(fixture.Backup, ZoneTreePersistenceFormat.IdentityFileName);
        var manifestPath = Path.Combine(fixture.Backup, ZoneTreePersistenceFormat.BackupManifestFileName);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(identityPath, IdentityVersionRepairFault.Create(
                original[ZoneTreePersistenceFormat.IdentityFileName], codecVersion), token);
            await MetadataTestFiles.UpdateManifestFileAsync(fixture.Backup, ZoneTreePersistenceFormat.IdentityFileName);
            var faulted = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            for (var repeat = 0; repeat < 2; repeat++)
            {
                await RefusedAsync(fixture, token);
                await BackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
                await BackupJournalRepairAssertions.SameCutAsync(source,
                    BackupJournalRepairAssertions.Read(fixture.Source, token));
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(identityPath,
            original[ZoneTreePersistenceFormat.IdentityFileName], CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(manifestPath,
            original[ZoneTreePersistenceFormat.BackupManifestFileName], CancellationToken.None), failures);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(() => HealthyAsync(fixture, source, original, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusedAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.VerifyBackup(fixture.Backup,
            UnitExecutionOptions.StorageExecution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(error.Message).IsEqualTo(ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeKey).GetString()).IsEqualTo(nameof(ErrorCode.FormatUnsupported));
        await Assert.That(problem.RootElement.GetProperty(DetailKey).GetString()).IsEqualTo(ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        await Assert.That(Directory.Exists(fixture.Destination)).IsFalse();
    }

    private static async Task HealthyAsync(BackupCutFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, Dictionary<string, byte[]> original,
        CancellationToken token)
    {
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        await Assert.That(OrleansSerialization.Serialize(verified.Identity).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(source.Identity))).IsTrue();
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var first = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, first.Identity, first);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, first.Identity);
        for (var cold = 0; cold < 2; cold++)
        {
            var reopened = BackupJournalRepairAssertions.Read(fixture.Destination, token);
            await BackupJournalRepairAssertions.SameCutAsync(first, reopened);
            await BackupJournalRepairAssertions.RestoredAsync(fixture, first.Identity, reopened);
        }
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
    }
}
