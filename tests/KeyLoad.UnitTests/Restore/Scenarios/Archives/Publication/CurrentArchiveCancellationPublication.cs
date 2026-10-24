using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class CurrentArchiveCancellationPublication
{
    private const string BackupSuffix = "-execution-cancellation";

    internal static async Task RunAsync(BackupCutFixture fixture, CancellationToken token)
    {
        var destination = fixture.Destination + BackupSuffix;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(store, destination, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReadCancelledAsync(string directory)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.VerifyBackup(
                    directory, UnitExecutionOptions.StorageExecution(), cancelled.Token));
                await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(ZoneTreeStore store, string destination, CancellationToken token)
    {
        var image = EventingArtifactState.FullBytes(store);
        var identity = OrleansSerialization.Serialize(store.Identity);
        var position = store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CaptureAndReadAsync(store, destination, position, image, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(EventingArtifactState.FullBytes(store))
                .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CaptureAndReadAsync(ZoneTreeStore store, string destination,
        long position, string[] image, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var original = Assert.ThrowsExactly<OperationCanceledException>(() =>
                    store.CreateBackup(destination, cancelled.Token));
                await Assert.That(original.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(Directory.Exists(destination)).IsFalse();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var captured = store.CreateBackup(destination, token);
        await Assert.That(captured).IsEqualTo(position);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(destination, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ReadCancelledAsync(destination);
            var read = ZoneTreeStore.VerifyBackup(destination, UnitExecutionOptions.StorageExecution(), token);
            await BackupPersistedPolicyAssertions.SameAsync(store.Identity, read.Identity);
            await Assert.That(read.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
            CurrentArchiveCancellationFlow.ArchiveAsync(archive, destination, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(store))
            .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
