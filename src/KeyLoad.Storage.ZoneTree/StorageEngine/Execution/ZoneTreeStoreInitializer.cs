namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreInitializer
{
    private const string OriginalPlainTreeMissing = "An existing storage identity requires its original plain tree.";
    internal static void Open(ZoneTreeStoreRuntime runtime)
    {
        EnsureStoreDirectory(runtime.Options.Directory);
        runtime.Ownership = ZoneTreeOwnerLock.Open(runtime.Options, requireExisting: false);
        var existing = File.Exists(Path.Combine(runtime.Options.Directory, ZoneTreePersistenceFormat.IdentityFileName));
        runtime.Identity = ZoneTreeIdentityFile.Open(runtime.Options, runtime.Ownership);
        OpenTree(runtime, requireExisting: existing);
        runtime.OwnershipSettlement.BeginConstructor();
        runtime.Maintainer = runtime.Tree.CreateMaintainer();
        runtime.PlainMaintainer = runtime.PlainTree.CreateMaintainer();
        runtime.OwnershipSettlement.ConstructorReturned();
    }

    internal static void OpenExisting(ZoneTreeStoreRuntime runtime, Guid expectedNodeId)
    {
        runtime.Ownership = ZoneTreeOwnerLock.Open(runtime.Options, requireExisting: true);
        runtime.Identity = ZoneTreeIdentityFile.OpenExisting(runtime.Options, expectedNodeId);
        OpenTree(runtime, requireExisting: true);
    }

    /// <summary>Opens the tree; ZoneTree replays its own write-ahead log and the store resumes at its persisted position.</summary>
    private static void OpenTree(ZoneTreeStoreRuntime runtime, bool requireExisting)
    {
        var plainDirectory = Path.Combine(runtime.Options.Directory, ZoneTreePersistenceFormat.PlainTreeDirectoryName);
        if (requireExisting && !Directory.Exists(plainDirectory))
        {
            throw new DirectoryNotFoundException(OriginalPlainTreeMissing);
        }
        runtime.OwnershipSettlement.BeginConstructor();
        runtime.Tree = ZoneTreeTreeFactory.Open(runtime.Options, requireExisting);
        runtime.OwnershipSettlement.ConstructorReturned();
        ZoneTreeWriteAheadLogAdmission.RequireProcessPersistence(runtime.Tree.Maintenance.ZoneTree);
        runtime.OwnershipSettlement.BeginConstructor();
        var factory = ZoneTreeTreeFactory.Plain(plainDirectory);
        ZoneTreeWriteAheadLogAdmission.RequireStoredProcessPersistence(factory);
        runtime.PlainTree = requireExisting ? factory.Open() : factory.OpenOrCreate();
        runtime.OwnershipSettlement.ConstructorReturned();
        ZoneTreeWriteAheadLogAdmission.RequireProcessPersistence(runtime.PlainTree);
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
