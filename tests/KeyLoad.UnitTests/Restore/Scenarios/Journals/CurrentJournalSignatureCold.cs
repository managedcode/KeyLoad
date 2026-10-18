using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentJournalSignatureCold
{
    private const string HealthyKey = "current-journal-signature/healthy";
    private const string HealthyValue = "healthy-after-exact-current-byte-repair";
    private const long HealthyPosition = 4L;

    internal static async Task RunAsync(NativeBackupCutFixture fixture, string target, StoreIdentity identity,
        CancellationToken token)
    {
        var restored = NativeBackupJournalRepairAssertions.Read(target, token);
        await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, identity, restored);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(target), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() =>
            {
                store.Commit((transaction, _) => { transaction.PutRecord(KeyCodec.Encode(HealthyKey), HealthyValue); return true; });
                return Task.CompletedTask;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var first = NativeBackupJournalRepairAssertions.Read(target, token);
        await RequireAsync(first, identity);
        var second = NativeBackupJournalRepairAssertions.Read(target, token);
        await RequireAsync(second, identity);
        await NativeBackupJournalRepairAssertions.SameCutAsync(first, second);
    }

    private static async Task RequireAsync((StoreIdentity Identity, long Position, string[] Rows) actual, StoreIdentity identity)
    {
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" + Convert.ToHexString(NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.LastAppliedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.ClockKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(true)),
            Convert.ToHexString(KeyCodec.Encode(HealthyKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(HealthyValue))
        ];
        await Assert.That(actual.Position).IsEqualTo(HealthyPosition);
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(JsonSerializer.Serialize(actual.Identity, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(identity, JsonDefaults.Options));
        await Assert.That(actual.Identity.DispatchPaused).IsTrue();
    }
}
