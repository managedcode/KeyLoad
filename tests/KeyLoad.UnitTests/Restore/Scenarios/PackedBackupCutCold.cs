using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class PackedBackupCutCold
{
    private const string HealthyKey = "packed-cut/healthy";
    private const string HealthyValue = "healthy-after-exact-original-repair";
    private const long AuthorityCommit = 1L;
    private const long HealthyCommit = 1L;

    internal static async Task RunAsync(string target, StoreIdentity identity, long capturedPosition,
        bool containsSecond, CancellationToken token)
    {
        var original = BackupJournalRepairAssertions.Read(target, token);
        await RequireAsync(original, identity, capturedPosition + AuthorityCommit, containsSecond, healthy: false);
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
        var expectedPosition = capturedPosition + AuthorityCommit + HealthyCommit;
        var first = BackupJournalRepairAssertions.Read(target, token);
        await RequireAsync(first, identity, expectedPosition, containsSecond, healthy: true);
        var second = BackupJournalRepairAssertions.Read(target, token);
        await RequireAsync(second, identity, expectedPosition, containsSecond, healthy: true);
        await BackupJournalRepairAssertions.SameCutAsync(first, second);
    }

    private static async Task RequireAsync((StoreIdentity Identity, long Position, string[] Rows) actual,
        StoreIdentity identity, long position, bool containsSecond, bool healthy)
    {
        var expected = new List<string> { Row(CliBackupRestoreFixture.StoredKey, CliBackupRestoreFixture.StoredValue),
            Row(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.DispatchPausedKey, true) };
        if (containsSecond)
        {
            expected.Add(Row(PackedBackupCutPair.SecondKey, PackedBackupCutPair.SecondValue));
        }
        if (healthy)
        {
            expected.Add(Row(HealthyKey, HealthyValue));
        }
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(actual.Position).IsEqualTo(position);
        await Assert.That(JsonSerializer.Serialize(actual.Identity, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(identity, JsonDefaults.Options));
        await Assert.That(actual.Identity.DispatchPaused).IsTrue();
    }

    private static string Row(string key, string value)
        => Convert.ToHexString(KeyCodec.Encode(key)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(value));
    private static string Row(string space, string key, bool value)
        => Convert.ToHexString(KeyCodec.Encode(space, key)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(value));
}
