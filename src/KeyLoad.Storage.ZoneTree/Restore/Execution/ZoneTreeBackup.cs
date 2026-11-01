using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ZoneTree.Backup;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;
using StorageKey = ZoneTree.Transactional.Versioning.VersionedKey<System.Memory<byte>>;
using StorageValue = ZoneTree.Transactional.Versioning.VersionedValue<System.Memory<byte>>;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Backs up and restores both native trees and the store identity.</summary>
internal static class ZoneTreeBackup
{
    private const string DestinationNotEmpty = "The backup or restore destination must be empty.";
    private const string DispatchPausedName = "dispatch-paused";
    private const string ValidIdentityRequired = "Restore requires a nonempty incarnation and a 32-byte signing key.";

    /// <summary>Writes a native snapshot generation per tree. The returned position precedes both independent captures.</summary>
    internal static async Task<long> CreateAsync(ZoneTreeStoreRuntime runtime, string directory,
        CancellationToken cancellationToken)
    {
        RequireEmpty(directory);
        cancellationToken.ThrowIfCancellationRequested();
        runtime.Check();
        ZoneTreeStoreFiles.CreatePrivateDirectory(directory);
        File.Copy(Path.Combine(runtime.Options.Directory, IdentityFileName), Path.Combine(directory, IdentityFileName));
        var position = runtime.Position;
        using var backup = new LiveBackup<StorageKey, StorageValue>(runtime.Tree.Maintenance.ZoneTree, new LiveBackupOptions
        {
            Store = new LocalLiveBackupProvider(Path.Combine(directory, TreeDirectoryName)),
            BackupAfterMerge = false,
            InMemoryMode = LiveBackupInMemoryMode.Snapshot
        });
        await backup.CreateGenerationAsync(cancellationToken);
        using var plain = runtime.PlainTree.CreateLiveBackup(new LiveBackupOptions
        {
            Store = new LocalLiveBackupProvider(Path.Combine(directory, PlainTreeDirectoryName)),
            BackupAfterMerge = false,
            InMemoryMode = LiveBackupInMemoryMode.Snapshot
        });
        await plain.CreateGenerationAsync(cancellationToken);
        return position;
    }

    /// <summary>Restores the latest backup generation under a new node identity with dispatch paused.</summary>
    internal static async Task<StoreIdentity> RestoreAsync(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation, byte[]? newSigningKey,
        CancellationToken cancellationToken)
    {
        var policy = executionOptions.Value;
        policy.Validate();
        RequireEmpty(destination);
        cancellationToken.ThrowIfCancellationRequested();
        var original = ZoneTreeIdentityFile.Read(Path.Combine(backup, IdentityFileName), policy.MaximumIdentityFileBytes,
            policy.StreamBufferBytes);
        if (newIncarnation == Guid.Empty || newSigningKey is { Length: not SigningKeyBytes })
        {
            throw Errors.Fail(ErrorCode.Validation, ValidIdentityRequired);
        }
        var identity = original with
        {
            NodeId = Guid.NewGuid(),
            Incarnation = newIncarnation ?? Guid.NewGuid(),
            SigningKey = newSigningKey?.ToArray() ?? RandomNumberGenerator.GetBytes(SigningKeyBytes),
            DispatchPaused = true
        };
        ZoneTreeStoreFiles.CreatePrivateDirectory(destination);
        ZoneTreeIdentityFile.Write(Path.Combine(destination, IdentityFileName), identity, policy.IdentityBufferBytes);
        // ZoneTree's restore takes no cancellation; once the tree is restored the pause must complete as well.
        var restored = await ZoneTreeTreeFactory.Storage(Path.Combine(destination, TreeDirectoryName))
            .RestoreFromLatestLiveBackup(Path.Combine(backup, TreeDirectoryName));
        restored.Dispose();
        var restoredPlain = await ZoneTreeTreeFactory.Plain(Path.Combine(destination, PlainTreeDirectoryName))
            .RestoreFromLatestLiveBackup(Path.Combine(backup, PlainTreeDirectoryName));
        restoredPlain.Dispose();
        await PauseDispatchAsync(destination, executionOptions);
        return identity;
    }

    // The restored records still carry the original node's dispatch state; the restored node starts paused.
    private static async Task PauseDispatchAsync(string destination, IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        var runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(destination).ResolveExecutionOptions(executionOptions));
        using var store = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedName), true);
            return true;
        });
        await store.SyncAsync(CancellationToken.None);
    }

    private static void RequireEmpty(string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, DestinationNotEmpty);
        }
    }
}
