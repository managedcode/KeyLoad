using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

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
            using var fixture = new NativeBackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, existingEmpty, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(NativeBackupCutFixture fixture, bool existingEmpty, CancellationToken token)
    {
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution());
        await BackupPersistedPolicyAssertions.SameAsync(source.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        if (existingEmpty)
        { Directory.CreateDirectory(fixture.Destination); }
        var parent = Path.GetDirectoryName(fixture.Destination) ?? throw new InvalidOperationException(MissingParent);
        var before = Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();
        await RefuseAsync(fixture);
        await Assert.That(Directory.Exists(fixture.Destination)).IsEqualTo(existingEmpty);
        await Assert.That(File.Exists(fixture.Destination)).IsFalse();
        if (existingEmpty)
        { await Assert.That(Directory.EnumerateFileSystemEntries(fixture.Destination).Any()).IsFalse(); }
        await Assert.That(Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        await HealthyAsync(fixture, token);
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAsync(NativeBackupCutFixture fixture)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var original = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeBackupRestoreRestore.Restore(
                    fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), null, null,
                    cancellationToken: cancelled.Token));
                await Assert.That(original.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HealthyAsync(NativeBackupCutFixture fixture, CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var actual = NativeBackupJournalRepairAssertions.Read(fixture.Destination, token);
        await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, actual.Identity, actual);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, actual.Identity);
        await NativeBackupJournalRepairAssertions.SameCutAsync(actual,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(actual,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
    }
}
