namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreInitializer
{
    internal static void Open(ZoneTreeStoreRuntime runtime)
    {
        EnsureStoreDirectory(runtime.Options.Directory);
        runtime.Ownership = ZoneTreeOwnerLock.Open(runtime.Options, requireExisting: false);
        runtime.Identity = ZoneTreeIdentityFile.Open(runtime.Options, runtime.Ownership);
        runtime.Journal = ZoneTreeStoreFiles.OpenJournal(runtime.Options);
        ZoneTreeJournalPreflight.Validate(runtime.Journal, runtime.Options, runtime.Identity.FormatVersion);
        OpenTree(runtime, requireExisting: false);
        ZoneTreeJournalRecovery.Recover(runtime);
        runtime.OwnershipSettlement.BeginConstructor();
        runtime.Maintenance = new(runtime.Tree, runtime.Options.MaintenanceExecution, TimeProvider.System);
        runtime.OwnershipSettlement.ConstructorReturned();
        ZoneTreeCheckpointReclaimer.Reclaim(runtime.Options.Directory);
    }

    internal static void OpenExisting(ZoneTreeStoreRuntime runtime, Guid expectedNodeId)
    {
        runtime.Ownership = ZoneTreeOwnerLock.Open(runtime.Options, requireExisting: true);
        runtime.Identity = ZoneTreeIdentityFile.OpenExisting(runtime.Options, expectedNodeId);
        runtime.Journal = ZoneTreeStoreFiles.OpenJournal(runtime.Options, FileMode.Open);
        ZoneTreeJournalPreflight.Validate(runtime.Journal, runtime.Options, runtime.Identity.FormatVersion);
        OpenTree(runtime, requireExisting: true);
        ZoneTreeJournalRecovery.Recover(runtime);
    }

    private static void OpenTree(ZoneTreeStoreRuntime runtime, bool requireExisting)
    {
        runtime.OwnershipSettlement.BeginConstructor();
        runtime.Tree = ZoneTreeTreeFactory.Open(runtime.Options, requireExisting);
        runtime.OwnershipSettlement.ConstructorReturned();
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
