using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns verified local backup and restore using one node-local store runtime.</summary>
internal sealed class ZoneTreeBackupRestore(ZoneTreeStoreRuntime runtime)
{
    internal long CreateBackup(string directory)
    {
        runtime.Gate.EnterWriteLock();
        try
        { runtime.Check(); return ZoneTreeBackupRestoreFiles.Create(runtime, directory); }
        finally { runtime.Gate.ExitWriteLock(); }
    }

    internal NativeCatalogBackupCapture CreateCatalogBackup(string directory,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Check();
            return CaptureNew(directory, capture, null, cancellationToken);
        }
        finally { runtime.Gate.ExitWriteLock(); }
    }

    internal NativeCatalogBackupCapture CaptureOrReadCatalogBackup(string directory, Action<IKeyValueView> authorize,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, Action<NativeCatalogBackupCapture> requireCaptured,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(requireCaptured);
        cancellationToken.ThrowIfCancellationRequested();
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Check();
            cancellationToken.ThrowIfCancellationRequested();
            authorize(runtime.View);
            ZoneTreeCatalogBackupPublication.RequirePath(directory);
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                var original = ZoneTreeCatalogBackupMetadataFile.ReadArchive(directory,
                    runtime.Options.MaintenanceExecution, cancellationToken);
                requireCaptured(original);
                cancellationToken.ThrowIfCancellationRequested();
                return original;
            }
            return CaptureNew(directory, capture, requireCaptured, cancellationToken);
        }
        finally { runtime.Gate.ExitWriteLock(); }
    }

    private NativeCatalogBackupCapture CaptureNew(string directory,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, Action<NativeCatalogBackupCapture>? requireCaptured,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = capture(runtime.View, runtime.Position, runtime.Identity);
        if (metadata is null || metadata.Length == ZoneTreeCatalogBackupPublication.EmptyMetadata)
        { throw Errors.Fail(ErrorCode.Validation, ZoneTreeCatalogBackupPublication.MissingMetadata); }
        if (metadata.Length > runtime.Options.MaximumBackupManifestBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ZoneTreeCatalogBackupPublication.MetadataExceeded); }
        var owned = metadata.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return ZoneTreeCatalogBackupPublication.Create(runtime, directory, owned, requireCaptured, cancellationToken);
    }

    internal static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation = null,
        byte[]? newSigningKey = null) => ZoneTreeBackupRestoreRestore.Restore(
            backup, destination, executionOptions, newIncarnation, newSigningKey);
}
