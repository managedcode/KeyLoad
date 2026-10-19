using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreDestinationCancellationPrecedenceTrial
{
    internal const string SentinelName = "original-sentinel.bin";
    private const string MissingParent = "The owned restore destination has no parent directory.";

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
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await BackupPersistedPolicyAssertions.SameAsync(source.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        Directory.CreateDirectory(fixture.Destination);
        var sentinel = Path.Combine(fixture.Destination, SentinelName);
        await File.WriteAllBytesAsync(sentinel, new byte[] { 0x4b, 0x4c, 0x10, 0x25 }, token);
        var parent = Path.GetDirectoryName(fixture.Destination) ?? throw new InvalidOperationException(MissingParent);
        var entries = Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();
        await RefusalsAsync(fixture, source, archive, parent, entries, token);
        await HealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefusalsAsync(BackupCutFixture fixture,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source,
        Dictionary<string, byte[]> archive, string parent, string[] entries, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var canceled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await canceled.CancelAsync();
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(
                        fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(),
                        cancellationToken: canceled.Token));
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
                    await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.RestoreDestinationNotEmptyDetail);
                    await RestoreDestinationCancellationPrecedenceAssertions.PreservedAsync(fixture, source, archive,
                        parent, entries, nonempty: true, token);
                }
                File.Delete(Path.Combine(fixture.Destination, SentinelName));
                var original = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.Restore(
                    fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(),
                    cancellationToken: canceled.Token));
                await Assert.That(original.CancellationToken).IsEqualTo(canceled.Token);
                await RestoreDestinationCancellationPrecedenceAssertions.PreservedAsync(fixture, source, archive,
                    parent, entries, nonempty: false, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HealthyAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var actual = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, actual.Identity, actual);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, actual.Identity);
        await BackupJournalRepairAssertions.SameCutAsync(actual,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(actual,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
    }
}
