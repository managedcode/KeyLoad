using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupRestoreRestore
{
    private const string RestoreTemporaryPrefix = ".keyload-restore-";
    private const string WrongPosition = "The recovered backup position does not match its verified manifest.";

    internal static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation, byte[]? newSigningKey,
        CancellationToken cancellationToken = default)
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
        var ownership = new ZoneTreeRestoreStagingOwnership();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = ZoneTreeBackupRestoreFiles.ReadAndVerify(backup, staging, policy,
                out var originalPosition, cancellationToken);
            var identity = CreateRestoredIdentity(original, newIncarnation, newSigningKey);
            ZoneTreeRestoreIdentityValidation.RequireFresh(original, identity);
            cancellationToken.ThrowIfCancellationRequested();
            ZoneTreeIdentityFile.Write(Path.Combine(staging, IdentityFileName), identity, policy.IdentityBufferBytes);
            var restoredIdentity = ApplyRestoreAuthorityState(staging, executionOptions, originalPosition,
                ownership, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Publish(staging, destinationPath);
            return restoredIdentity;
        }
        catch (Exception primary)
        {
            try
            {
                if (ownership.IsSettled && Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
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
        ZoneTreeBackupPathValidation.Require(destination);
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
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, long originalPosition,
        ZoneTreeRestoreStagingOwnership ownership, CancellationToken cancellationToken)
    {
        ownership.BeginOpen();
        var options = new ZoneTreeStoreOptions(destination).ResolveExecutionOptions(executionOptions);
        var runtime = new ZoneTreeStoreRuntime(options);
        var restored = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        Exception? primary = null;
        try
        {
            if (restored.Position != originalPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, WrongPosition);
            }
            cancellationToken.ThrowIfCancellationRequested();
            restored.Commit((tx, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                tx.Delete(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
                tx.Delete(KeyCodec.Encode(SystemNamespace, ClockKey));
                tx.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedKey), true);
                return true;
            });
            return restored.Identity;
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            DisposeRestoredStore(restored, primary, ownership);
        }
    }

    private static void DisposeRestoredStore(ZoneTreeStore restored, Exception? primary,
        ZoneTreeRestoreStagingOwnership ownership)
    {
        try
        {
            restored.Dispose();
            ownership.CompleteClose();
        }
        catch (Exception cleanup)
        {
            if (primary is null)
            {
                throw;
            }
            throw new AggregateException(primary, cleanup);
        }
    }
}
