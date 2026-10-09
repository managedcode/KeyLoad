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
            var identity = CreateRestoredIdentity(ZoneTreeBackupRestoreFiles.ReadAndVerify(backup, staging, policy),
                newIncarnation, newSigningKey);
            ZoneTreeIdentityFile.Write(Path.Combine(staging, IdentityFileName), identity, policy.IdentityBufferBytes);
            var restoredIdentity = ApplyRestoreAuthorityState(staging, executionOptions);
            Publish(staging, destinationPath);
            return restoredIdentity;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
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
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        var runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(destination).ResolveExecutionOptions(executionOptions));
        using var restored = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        restored.Commit((tx, _) =>
        {
            tx.Delete(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
            tx.Delete(KeyCodec.Encode(SystemNamespace, ClockKey));
            tx.Delete(KeyCodec.Encode(MembershipNamespace, OrleansMembershipKey));
            tx.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedKey), true);
            return true;
        });
        return restored.Identity;
    }
}
