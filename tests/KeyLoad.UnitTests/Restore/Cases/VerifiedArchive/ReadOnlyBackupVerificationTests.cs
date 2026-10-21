using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class ReadOnlyBackupVerificationTests
{
    private const int FirstByte = 0;
    private const int FirstAttempt = 0;
    private const int OneByte = 1;
    private const int Attempts = 2;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadOnlyArchiveAndParentVerifyExactCutWithoutCreatingEntries(bool checkpoint)
    {
        using var fixture = new BackupCutFixture(checkpoint);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var parent = Path.GetDirectoryName(fixture.Backup)!;
        var entries = Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray();
        using var permissions = new ReadOnlyBackupPermissionFixture(fixture.Backup);
        permissions.Protect();
        await RequireHealthyAsync(fixture, token);
        await RequireHealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
        await Assert.That(Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(entries, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadOnlyChecksumOrLengthFailurePreservesBytesThenRepairedArchiveVerifies(bool lengthMismatch)
    {
        using var fixture = new BackupCutFixture(checkpoint: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var altered = await File.ReadAllBytesAsync(fixture.Journal, token);
        if (lengthMismatch)
        {
            Array.Resize(ref altered, altered.Length + OneByte);
        }
        else
        {
            altered[FirstByte] ^= OneByte;
        }
        await File.WriteAllBytesAsync(fixture.Journal, altered, token);
        var damaged = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        using (var permissions = new ReadOnlyBackupPermissionFixture(fixture.Backup))
        {
            permissions.Protect();
            for (var attempt = FirstAttempt; attempt < Attempts; attempt++)
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                    ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileVerificationFailedDetail);
            }
            await BackupJournalRepairAssertions.SameArchiveAsync(damaged, fixture.Backup, token);
        }
        await BackupJournalRepairAssertions.RepairArchiveAsync(fixture.Backup, original);
        using var repairedPermissions = new ReadOnlyBackupPermissionFixture(fixture.Backup);
        repairedPermissions.Protect();
        await RequireHealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
    }

    [Test]
    public async Task CancelledReadOnlyVerificationPreservesArchiveThenHealthyReadSucceeds()
    {
        using var fixture = new BackupCutFixture(checkpoint: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        using var permissions = new ReadOnlyBackupPermissionFixture(fixture.Backup);
        permissions.Protect();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() =>
            ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), cancelled.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
        await RequireHealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
    }

    [Test]
    public async Task LinkedJournalRefusesWithOriginalDetailThenExactRepairVerifies()
    {
        const string HeldJournal = "held-journal.wal";
        using var fixture = new BackupCutFixture(checkpoint: true);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var held = Path.Combine(Path.GetDirectoryName(fixture.Backup)!, HeldJournal);
        File.Move(fixture.Journal, held);
        File.CreateSymbolicLink(fixture.Journal, held);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileLinkDetail);
        File.Delete(fixture.Journal);
        File.Move(held, fixture.Journal);
        await RequireHealthyAsync(fixture, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.Backup, token);
    }

    private static async Task RequireHealthyAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await BackupPersistedPolicyAssertions.SameAsync(fixture.Identity, verified.Identity);
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
    }
}
