using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupJournalRepairTrial
{
    internal static async Task RunAsync(bool checkpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        await RefuseAndRepairAsync(fixture, archive, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
        await Assert.That(OrleansSerialization.Serialize(verified.Identity)
            .SequenceEqual(OrleansSerialization.Serialize(fixture.Identity))).IsTrue();
        token.ThrowIfCancellationRequested();
        var identity = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        var restored = BackupJournalRepairAssertions.Read(fixture.Destination, token);
        await BackupJournalRepairAssertions.RestoredAsync(fixture, identity, restored);
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(restored,
            BackupJournalRepairAssertions.Read(fixture.Destination, token));
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.Source, token));
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAndRepairAsync(BackupCutFixture fixture,
        Dictionary<string, byte[]> archive, CancellationToken token)
    {
        var originalJournal = archive[MetadataTestContract.JournalFileName];
        var source = BackupJournalRepairAssertions.Read(fixture.Source, token);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var damaged = originalJournal.ToArray();
            damaged[BackupCutFixture.LastFrameOffset(damaged)
                + ZoneTreePersistenceFormat.ChecksumOffset] ^= 1;
            repairRequired = true;
            await fixture.RewriteJournalAsync(damaged);
            var faulted = await BackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
                ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution()));
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(rejected.Message).IsEqualTo(ZoneTreePersistenceFormat.JournalChecksumInvalid);
            await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
            await BackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
            await BackupJournalRepairAssertions.SameCutAsync(source,
                BackupJournalRepairAssertions.Read(fixture.Source, token));
        }, failures);
        if (repairRequired)
        {
            await ServerFailureObserver.ObserveAsync(() =>
                File.WriteAllBytesAsync(fixture.Journal, originalJournal, CancellationToken.None), failures);
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
                Path.Combine(fixture.Backup, MetadataTestContract.ManifestFileName),
                archive[MetadataTestContract.ManifestFileName], CancellationToken.None), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
