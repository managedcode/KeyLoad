using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentArchiveCancellationRefusal
{
    private const string VerificationSuffix = "-cancelled-verification";

    internal static async Task RunAsync(NativeBackupCutFixture fixture, bool existingEmpty, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (existingEmpty)
        {
            Directory.CreateDirectory(fixture.Destination);
        }
        var parent = Path.GetDirectoryName(fixture.Destination) ?? throw new DirectoryNotFoundException();
        var originalEntries = Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                await NativeStagesAsync(fixture, cancelled.Token);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var failure = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeBackupRestoreRestore.Restore(
                        fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), null, null,
                        cancellationToken: cancelled.Token));
                    await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
                    await Assert.That(Directory.Exists(fixture.Destination)).IsEqualTo(existingEmpty);
                    if (existingEmpty)
                    {
                        await Assert.That(Directory.EnumerateFileSystemEntries(fixture.Destination).Any()).IsFalse();
                    }
                }
            }, failures);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(originalEntries, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task NativeStagesAsync(NativeBackupCutFixture fixture, CancellationToken cancelled)
    {
        var stage = fixture.Destination + VerificationSuffix;
        Directory.CreateDirectory(stage);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var copy = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeBackupRestoreFiles.ReadAndVerify(
                fixture.Backup, stage, UnitExecutionOptions.StorageExecution().Value, out _, cancelled));
            await Assert.That(copy.CancellationToken).IsEqualTo(cancelled);
            await Assert.That(Directory.EnumerateFileSystemEntries(stage).Any()).IsFalse();
            using var journal = File.OpenRead(fixture.Journal);
            var validation = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeBackupJournalValidation.Verify(
                journal, fixture.Identity, fixture.Position, UnitExecutionOptions.StorageExecution().Value, cancelled));
            await Assert.That(validation.CancellationToken).IsEqualTo(cancelled);
            await Assert.That(journal.Position).IsEqualTo(0L);
        }, failures);
        ServerFailureObserver.Observe(() => Directory.Delete(stage, recursive: false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
