using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupPieceBoundaryCold
{
    internal static async Task RunAsync(CliBackupRestoreFixture fixture, CliBackupRestoreProcessResult result,
        string restored, byte[] key, string payload, CancellationToken token)
    {
        var original = NativeBackupJournalRepairAssertions.Read(restored, token);
        await ReadOwnedAsync(restored, key, payload);
        await NativeBackupJournalRepairAssertions.SameCutAsync(original,
            NativeBackupJournalRepairAssertions.Read(restored, token));
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, result, restored);
        await ReadOwnedAsync(restored, key, payload);
        await NativeBackupJournalRepairAssertions.SameCutAsync(original,
            NativeBackupJournalRepairAssertions.Read(restored, token));
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, result, restored);
    }

    private static async Task ReadOwnedAsync(string restored, byte[] key, string payload)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(restored), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var value = store.Read(view => global::KeyLoad.NativeSerialization.Deserialize<string>(view.ReadOwnedValue(key)!));
                await Assert.That(value).IsEqualTo(payload);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
