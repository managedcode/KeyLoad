using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeBackupJournalRepairTrial
{
    internal static async Task RunAsync(bool checkpoint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new NativeBackupCutFixture(checkpoint);
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(NativeBackupCutFixture fixture, CancellationToken token)
    {
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        await RefuseAndRepairAsync(fixture, archive, token);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
        await Assert.That(NativeSerialization.Serialize(verified.Identity)
            .SequenceEqual(NativeSerialization.Serialize(fixture.Identity))).IsTrue();
        token.ThrowIfCancellationRequested();
        var identity = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution(), cancellationToken: token);
        var restored = NativeBackupJournalRepairAssertions.Read(fixture.Destination, token);
        await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, identity, restored);
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAndRepairAsync(NativeBackupCutFixture fixture,
        Dictionary<string, byte[]> archive, CancellationToken token)
    {
        var originalJournal = archive[MetadataTestContract.JournalFileName];
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var damaged = originalJournal.ToArray();
            damaged[NativeBackupCutFixture.LastFrameOffset(damaged)
                + ZoneTreePersistenceFormat.ChecksumOffset] ^= 1;
            repairRequired = true;
            await fixture.RewriteJournalAsync(damaged);
            var faulted = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
                ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution()));
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(rejected.Message).IsEqualTo(ZoneTreePersistenceFormat.JournalChecksumInvalid);
            await fixture.AssertRejectedUnchangedAsync(ErrorCode.Corruption);
            await NativeBackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
            await NativeBackupJournalRepairAssertions.SameCutAsync(source,
                NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
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
