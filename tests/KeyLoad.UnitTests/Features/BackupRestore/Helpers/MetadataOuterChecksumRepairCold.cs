using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataOuterChecksumRepairCold
{
    private const string HealthyKey = "metadata-checksum-repaired-healthy";
    private const string HealthyValue = "{\"repaired\":true}";
    private const string MissingValue = "The repaired metadata archive lost its complete native value.";
    private const int ColdOwners = 2;
    private const long HealthyCommitCount = 1;

    internal static async Task RunAsync(string directory, CancellationToken token)
    {
        string[] image = [];
        byte[] identity = [];
        long position = 0;
        await OwnedAsync(directory, async store =>
        {
            var before = store.Position;
            await Assert.That(store.Commit((transaction, _) =>
                {
                    transaction.PutRecord(KeyCodec.Encode(HealthyKey), HealthyValue);
                    return true;
                })).IsTrue();
            await Assert.That(store.Position).IsEqualTo(before + HealthyCommitCount);
            await ValuesAsync(store);
            image = EventingArtifactState.FullBytes(store);
            identity = NativeSerialization.Serialize(store.Identity);
            position = store.Position;
        }, token);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await OwnedAsync(directory, async store =>
            {
                await Assert.That(store.Position).IsEqualTo(position);
                await Assert.That(EventingArtifactState.FullBytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
                await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
                await ValuesAsync(store);
            }, token);
        }
    }

    private static async Task ValuesAsync(ZoneTreeStore store)
    {
        var original = store.Read(view => view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes))
            ?? throw new InvalidOperationException(MissingValue);
        var healthy = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(HealthyKey)))
            ?? throw new InvalidOperationException(MissingValue);
        await Assert.That(NativeSerialization.Deserialize<string>(original)).IsEqualTo(MetadataBackupFixture.ExpectedValue);
        await Assert.That(NativeSerialization.Deserialize<string>(healthy)).IsEqualTo(HealthyValue);
        await Assert.That(store.Identity.DispatchPaused).IsTrue();
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
