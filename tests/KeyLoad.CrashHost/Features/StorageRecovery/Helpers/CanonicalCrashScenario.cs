using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CanonicalCrashScenario
{
    internal static void Run(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        store.Commit((transaction, _) =>
        {
            for (var index = 0; index < CrashFixtureValues.ItemCount; index++)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ItemKey, (long)index), 0);
            }
            if (mode == CrashFixtureValues.InstallMode)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ObsoleteKey), true);
            }
            return true;
        });
        store.Commit((transaction, _) =>
        {
            for (var index = 0; index < CrashFixtureValues.ItemCount; index++)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ItemKey, (long)index), 1);
            }
            return true;
        });
        if (mode == CrashFixtureValues.CompactMode)
        {
            boundary.Armed = true;
            store.Compact();
        }
        else if (mode == CrashFixtureValues.InstallMode)
        {
            var snapshot = CreateSnapshot(directory, store);
            boundary.Armed = true;
            store.InstallSnapshot(snapshot, CrashFixtureValues.SnapshotCut);
        }
    }

    private static string CreateSnapshot(string directory, ZoneTreeStore store)
    {
        var snapshot = Path.Combine(directory, CrashFixtureValues.IncomingSnapshotFile);
        using var source = new ZoneTreeStore(new(Path.Combine(directory, CrashFixtureValues.SnapshotSourceDirectory))
        { Incarnation = store.Identity.Incarnation, SigningKey = store.Identity.SigningKey }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        source.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.System, CrashFixtureValues.AppliedKey), CrashFixtureValues.SnapshotPreviousCut);
            return true;
        });
        source.Commit((transaction, _) =>
        {
            for (var index = 0; index < CrashFixtureValues.ItemCount; index++)
            {
                transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.ItemKey, (long)index), 2);
            }
            transaction.PutRecord(KeyCodec.Encode(CrashFixtureValues.System, CrashFixtureValues.AppliedKey), CrashFixtureValues.SnapshotCut);
            return true;
        });
        source.CreateSnapshot(snapshot, CrashFixtureValues.SnapshotCut);
        return snapshot;
    }
}
