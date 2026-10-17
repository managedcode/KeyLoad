using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentArchiveCancellationFlow
{
    internal static async Task RunAsync(bool checkpoint, bool existingEmpty, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new NativeBackupCutFixture(checkpoint);
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(fixture, existingEmpty, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(NativeBackupCutFixture fixture, bool existingEmpty, CancellationToken token)
    {
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CurrentArchiveCancellationPublication.RunAsync(fixture, token), failures);
        await ServerFailureObserver.ObserveAsync(() => CurrentArchiveCancellationRefusal.RunAsync(fixture, existingEmpty, token), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            ArchiveAsync(archive, fixture.Backup, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SourceAsync(source, fixture.Source, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution());
            await BackupPersistedPolicyAssertions.SameAsync(source.Identity, verified.Identity);
            await Assert.That(verified.Position).IsEqualTo(source.Position);
            var identity = ZoneTreeStore.Restore(fixture.Backup, fixture.Destination, UnitExecutionOptions.StorageExecution());
            await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, identity,
                NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
            await CurrentArchiveCancellationHealthy.RunAsync(fixture.Destination, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SourceAsync(source, fixture.Source, token), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            ArchiveAsync(archive, fixture.Backup, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SourceAsync((KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) original,
        string directory, CancellationToken token)
    {
        var current = NativeBackupJournalRepairAssertions.Read(directory, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(current.Position).IsEqualTo(original.Position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(NativeSerialization.Serialize(current.Identity).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(original.Identity))).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(current.Rows).IsEquivalentTo(original.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ArchiveAsync(Dictionary<string, byte[]> original, string directory, CancellationToken token)
    {
        var current = await NativeBackupJournalRepairAssertions.ArchiveAsync(directory, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(current.Keys).IsEquivalentTo(original.Keys);
        }, failures);
        foreach (var row in original)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(current.TryGetValue(row.Key, out var bytes)).IsTrue();
                await Assert.That(bytes!).IsEquivalentTo(row.Value, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
