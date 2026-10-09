using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupRestoreRestore
{
    private const string RestoreTemporaryPrefix = ".keyload-restore-";

    internal static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation, byte[]? newSigningKey)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        var policy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        EnsureDestinationIsEmpty(destination);
        var destinationPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var staging = Path.Combine(Path.GetDirectoryName(destinationPath)!,
            RestoreTemporaryPrefix + Guid.NewGuid().ToString(GuidFormat));
        ZoneTreeStoreFiles.CreatePrivateDirectory(staging);
        try
        {
            var originalIdentity = ZoneTreeBackupRestoreFiles.ReadAndVerify(backup, staging, policy);
            var identity = CreateRestoredIdentity(originalIdentity, newIncarnation, newSigningKey);
            ZoneTreeIdentityFile.Write(Path.Combine(staging, IdentityFileName), identity, policy.IdentityBufferBytes);
            var restoredIdentity = ApplyRestoreAuthorityState(staging, executionOptions, originalIdentity.Incarnation);
            Publish(staging, destinationPath);
            return restoredIdentity;
        }
        catch (Exception primary)
        {
            try
            {
                if (Directory.Exists(staging)) { Directory.Delete(staging, recursive: true); }
            }
            catch (Exception cleanup)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private static void Publish(string staging, string destination)
    {
        EnsureDestinationIsEmpty(destination);
        var existed = Directory.Exists(destination);
        if (existed)
        {
            Directory.Delete(destination, recursive: false);
        }
        try
        {
            Directory.Move(staging, destination);
        }
        catch (Exception)
        {
            if (existed && !Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }
            throw;
        }
    }

    private static void EnsureDestinationIsEmpty(string destination)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, RestoreDestinationNotEmpty);
        }
    }

    private static StoreIdentity CreateRestoredIdentity(StoreIdentity identity, Guid? newIncarnation,
        byte[]? newSigningKey)
    {
        return identity with
        {
            NodeId = Guid.NewGuid(),
            Incarnation = newIncarnation ?? Guid.NewGuid(),
            SigningKey = newSigningKey?.ToArray() ?? RandomNumberGenerator.GetBytes(SigningKeyBytes),
            DispatchPaused = true
        };
    }

    private static StoreIdentity ApplyRestoreAuthorityState(string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid sourceIncarnation)
    {
        var runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(destination).ResolveExecutionOptions(executionOptions));
        var restored = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        Exception? primary = null;
        try
        {
            var hasHistoricalOrigins = ZoneTreeBackupRestoreRoster.CarryOrigins(restored, sourceIncarnation);
            restored.Commit((tx, _) =>
            {
                tx.Delete(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
                tx.Delete(KeyCodec.Encode(SystemNamespace, ClockKey));
                tx.Delete(KeyCodec.Encode(MembershipNamespace, OrleansMembershipKey));
                tx.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedKey), true);
                if (hasHistoricalOrigins)
                {
                    tx.PutRecord(AtomicPartitionRosterRestoreOriginSerialization.IdentityKey(),
                        new AtomicPartitionRosterRestoreIdentity(AtomicPartitionRosterRestoreOriginSerialization.CurrentVersion,
                            sourceIncarnation, restored.Identity.Incarnation));
                }
                return true;
            });
            return restored.Identity;
        }
        catch (Exception failure)
        { primary = failure; throw; }
        finally { DisposeRestoredStore(restored, primary); }
    }

    private static void DisposeRestoredStore(ZoneTreeStore restored, Exception? primary)
    {
        try { restored.Dispose(); }
        catch (Exception cleanup)
        {
            if (primary is null) { throw; }
            throw new AggregateException(primary, cleanup);
        }
    }
}
