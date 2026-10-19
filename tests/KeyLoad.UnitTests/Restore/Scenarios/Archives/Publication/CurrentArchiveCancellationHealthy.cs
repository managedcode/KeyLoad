using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentArchiveCancellationHealthy
{
    private const string HealthyKey = "current-archive-cancellation-healthy";
    private const string HealthyValue = "{\"currentArchive\":true}";
    private const int ColdOwners = 2;

    internal static async Task RunAsync(string directory, CancellationToken token)
    {
        string[] image = [];
        byte[] identity = [];
        long position = 0;
        await OwnedAsync(directory, async store =>
        {
            var originalPosition = store.Position;
            await Assert.That(store.Commit((transaction, _) =>
            {
                transaction.PutRecord(KeyCodec.Encode(HealthyKey), HealthyValue);
                return true;
            })).IsTrue();
            await Assert.That(store.Position).IsEqualTo(originalPosition + 1L);
            await ValuesAsync(store);
            image = EventingArtifactState.FullBytes(store);
            identity = OrleansSerialization.Serialize(store.Identity);
            position = store.Position;
        }, token);
        var coldFailures = new List<Exception>();
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await ServerFailureObserver.ObserveAsync(() => OwnedAsync(directory, async store =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(store.Position).IsEqualTo(position);
                }, failures);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(EventingArtifactState.FullBytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
                }, failures);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => ValuesAsync(store), failures);
                ServerFailureObserver.ThrowIfAny(failures);
            }, token), coldFailures);
        }
        ServerFailureObserver.ThrowIfAny(coldFailures);
    }

    private static async Task ValuesAsync(ZoneTreeStore store)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Read(view => view.GetRecord<string>(MetadataBackupFixture.StoredKeyBytes)))
                .IsEqualTo(MetadataBackupFixture.ExpectedValue);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Read(view => view.GetRecord<string>(KeyCodec.Encode(HealthyKey))))
                .IsEqualTo(HealthyValue);
        }, failures);
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" +
                Convert.ToHexString(OrleansSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(HealthyKey)) + ":" +
                Convert.ToHexString(OrleansSerialization.Serialize(HealthyValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.LastAppliedKey)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.ClockKey)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(true))
        ];
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(EventingArtifactState.FullBytes(store))
                .IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Identity.FormatVersion).IsEqualTo(7);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Identity.MinimumReaderContract).IsEqualTo(1);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Identity.DispatchPaused).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OwnedAsync(string directory, Func<ZoneTreeStore, Task> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => operation(store), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
