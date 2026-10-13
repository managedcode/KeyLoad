using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreUnsafeDestinationCold
{
    private const string HealthyNamespace = "backup-path";
    private const string HealthyKey = "healthy";
    private const string HealthyValue = "restored-path-healthy";

    internal static async Task RunAsync(NativeBackupCutFixture fixture, string destination,
        StoreIdentity identity, CancellationToken token)
    {
        await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Identity.Incarnation);
        await Assert.That(identity.NodeId).IsNotEqualTo(fixture.Identity.NodeId);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(fixture.Identity.SigningKey.Span)).IsFalse();
        for (var cold = 0; cold < 3; cold++)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                using var store = new ZoneTreeStore(new(destination), UnitExecutionOptions.StorageExecution(),
                    UnitExecutionOptions.PointCacheExecution());
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(JsonSerializer.Serialize(store.Identity, JsonDefaults.Options))
                        .IsEqualTo(JsonSerializer.Serialize(identity, JsonDefaults.Options));
                    await Assert.That(store.Identity.DispatchPaused).IsTrue();
                    await Assert.That(store.Position).IsEqualTo(fixture.Position + (cold == 0 ? 1L : 2L));
                    await ImageAsync(store, healthy: cold != 0);
                    if (cold == 0)
                    {
                        store.Commit((transaction, _) =>
                        { transaction.PutRecord(KeyCodec.Encode(HealthyNamespace, HealthyKey), HealthyValue); return true; });
                        await Assert.That(store.Position).IsEqualTo(fixture.Position + 2L);
                        await ImageAsync(store, healthy: true);
                    }
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task ImageAsync(ZoneTreeStore store, bool healthy)
    {
        var expected = new List<string>
        {
            Row(MetadataBackupFixture.StoredKeyBytes, NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Row(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.DispatchPausedKey),
                NativeSerialization.Serialize(true))
        };
        if (healthy)
        { expected.Add(Row(KeyCodec.Encode(HealthyNamespace, HealthyKey), NativeSerialization.Serialize(HealthyValue))); }
        var actual = QueueWholeFlowStorage.Bytes(store);
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    private static string Row(byte[] key, byte[] value)
        => Convert.ToHexString(key) + ":" + Convert.ToHexString(value);
}
