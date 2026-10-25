using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class VerifiedArchiveCancellationTrial
{
    private const string RestoreCommand = "restore";
    private const string MissingParent = "The owned restore destination has no parent directory.";

    internal static async Task RunAsync(bool existingEmpty, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, existingEmpty, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, bool existingEmpty, CancellationToken token)
    {
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await BackupPersistedPolicyAssertions.SameAsync(source.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        if (existingEmpty)
        {
            Directory.CreateDirectory(fixture.Destination);
        }
        var parent = Path.GetDirectoryName(fixture.Destination) ?? throw new InvalidOperationException(MissingParent);
        var before = Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();
        await RefuseAsync(fixture);
        await Assert.That(Directory.Exists(fixture.Destination)).IsEqualTo(existingEmpty);
        await Assert.That(File.Exists(fixture.Destination)).IsFalse();
        if (existingEmpty)
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(fixture.Destination).Any()).IsFalse();
        }
        await Assert.That(Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        await HealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAsync(BackupCutFixture fixture)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var original = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.Restore(
                    fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(),
                    cancellationToken: cancelled.Token));
                await Assert.That(original.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HealthyAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
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
