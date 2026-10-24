using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class TruncatedBackupArchiveCold
{
    private const string HealthyKey = "archive-repaired-healthy";
    private const string HealthyValue = "{\"restored\":true}";
    private const string MissingHealthy = "The repaired original archive lost its healthy execution commit.";
    private const int ColdOwners = 2;
    private const string LargeKey = "archive-original-bounded-pieces";
    private const int LargeCharacters = 8192;
    private const char LargeCharacter = 'x';
    private const string MissingLarge = "The original archive lost its complete bounded-piece value.";

    internal static async Task SeedAsync(string directory)
        => await OwnedAsync(directory, async store =>
        {
            var value = new string(LargeCharacter, LargeCharacters);
            await Assert.That(store.Commit((transaction, _) =>
                {
                    transaction.PutRecord(KeyCodec.Encode(LargeKey), value);
                    return true;
                })).IsTrue();
            await LargeAsync(store);
        });

    private static async Task LargeAsync(ZoneTreeStore store)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(LargeKey)))
            ?? throw new InvalidOperationException(MissingLarge);
        await Assert.That(OrleansSerialization.Deserialize<string>(bytes))
            .IsEqualTo(new string(LargeCharacter, LargeCharacters));
    }

    internal static async Task RunAsync(string directory)
    {
        string[] image = [];
        var identity = string.Empty;
        long position = 0;
        await OwnedAsync(directory, async store =>
        {
            var before = store.Position;
            await Assert.That(store.Commit((transaction, _) =>
                {
                    transaction.PutRecord(KeyCodec.Encode(HealthyKey), HealthyValue);
                    return true;
                })).IsTrue();
            await Assert.That(store.Position).IsEqualTo(before + 1);
            await HealthyAsync(store);
            image = EventingArtifactState.FullBytes(store);
            identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
            position = store.Position;
        });
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            await OwnedAsync(directory, async store =>
            {
                await Assert.That(store.Position).IsEqualTo(position);
                await Assert.That(EventingArtifactState.FullBytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
                await Assert.That(JsonSerializer.Serialize(store.Identity, JsonDefaults.Options)).IsEqualTo(identity);
                await HealthyAsync(store);
            });
        }
    }

    private static async Task HealthyAsync(ZoneTreeStore store)
    {
        var bytes = store.Read(view => view.ReadOwnedValue(KeyCodec.Encode(HealthyKey)))
            ?? throw new InvalidOperationException(MissingHealthy);
        await Assert.That(OrleansSerialization.Deserialize<string>(bytes)).IsEqualTo(HealthyValue);
        await Assert.That(store.Identity.DispatchPaused).IsTrue();
        await LargeAsync(store);
    }

    private static async Task OwnedAsync(string directory, Func<ZoneTreeStore, Task> operation)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => operation(store), failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
