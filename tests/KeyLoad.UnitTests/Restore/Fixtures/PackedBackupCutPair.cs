using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class PackedBackupCutPair
{
    internal const string SecondKey = "packed-cut/second";
    internal const string SecondValue = "second-acknowledged-execution-cut";
    private const string FirstBackup = "first-execution-backup";
    private const string SecondBackup = "second-execution-backup";

    internal static async Task<(string First, string Second)> CaptureAsync(CliBackupRestoreFixture fixture)
    {
        var first = Path.Combine(fixture.Root, FirstBackup);
        var second = Path.Combine(fixture.Root, SecondBackup);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.SourceDirectory), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                store.CreateBackup(first);
                var before = store.Position;
                store.Commit((transaction, _) => { transaction.PutRecord(KeyCodec.Encode(SecondKey), SecondValue); return true; });
                await Assert.That(store.Position).IsEqualTo(before + 1L);
                store.CreateBackup(second);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return (first, second);
    }
}
