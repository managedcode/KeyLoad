using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreInitializer
{
    internal static void Open(ZoneTreeStoreRuntime runtime)
    {
        EnsureStoreDirectory(runtime.Options.Directory);
        try
        {
            runtime.Ownership = new FileStream(Path.Combine(runtime.Options.Directory, OwnerLockFileName),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            runtime.Identity = ZoneTreeIdentityFile.Open(runtime.Options);
            runtime.Tree = ZoneTreeTreeFactory.Open(runtime.Options);
            runtime.Journal = ZoneTreeStoreFiles.OpenJournal(runtime.Options);
            ZoneTreeJournalRecovery.Recover(runtime);
            runtime.Maintainer = runtime.Tree.CreateMaintainer();
            ZoneTreeCheckpointReclaimer.Reclaim(runtime.Options.Directory);
        }
        catch (Exception)
        {
            ZoneTreeStoreHandleDisposal.FailedOpen(runtime);
            throw;
        }
    }

    private static void EnsureStoreDirectory(string directory)
    {
        if (!Directory.Exists(directory) && !OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        Directory.CreateDirectory(directory);
    }
}
