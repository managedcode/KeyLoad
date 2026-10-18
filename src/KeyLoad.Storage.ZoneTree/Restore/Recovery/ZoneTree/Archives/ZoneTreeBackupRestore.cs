using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns verified local backup and restore using one silo-local store runtime.</summary>
internal sealed class ZoneTreeBackupRestore(ZoneTreeStoreRuntime runtime)
{
    internal long CreateBackup(string directory)
    {
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Check();
            return ZoneTreeBackupRestoreFiles.Create(runtime, directory);
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }

    internal static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation = null,
        byte[]? newSigningKey = null) => ZoneTreeBackupRestoreRestore.Restore(
            backup, destination, executionOptions, newIncarnation, newSigningKey);
}
