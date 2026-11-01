using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal static class CanonicalCrashScenario
{
    internal static async Task RunAsync(ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int CompileEmptyCount = 0;
        const int ValueEmptyCount = 0;
        const int ValueSingleItemCount = 1;

        store.Commit((transaction, _) =>
        {
            for (var index = CompileEmptyCount; index < CrashFixtureValues.ItemCount; index++)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ItemKey, (long)index), ValueEmptyCount);
            }
            return true;
        });
        await boundary.ArmAsync(store);
        store.Commit((transaction, _) =>
        {
            for (var index = CompileEmptyCount; index < CrashFixtureValues.ItemCount; index++)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ItemKey, (long)index), ValueSingleItemCount);
            }
            return true;
        });
    }
}
