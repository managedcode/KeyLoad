using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Creates an archive and bounded catalog metadata under the same native physical gate.</summary>
public interface INativeCatalogBackupStore
{
    /// <summary>Captures a complete independently owned native metadata record with the original archive cut.</summary>
    /// <param name="directory">Clean private target owned by this backup invocation.</param>
    /// <param name="capture">Synchronous metadata reader; it must not retain the gated view or re-enter the store.</param>
    /// <param name="cancellationToken">Original owning operation cancellation.</param>
    /// <returns>The actual archived store position and exact captured metadata.</returns>
    NativeCatalogBackupCapture CreateCatalogBackup(string directory,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, CancellationToken cancellationToken);

    /// <summary>Authenticates and admits a concurrent immutable replay under the same native owning write gate.</summary>
    /// <param name="directory">Exact stable capture path; never a consumer-selected fallback path.</param>
    /// <param name="authorize">Fresh persisted authorization over the borrowed current gated view.</param>
    /// <param name="capture">New same-view bounded metadata capture; no store re-entry or retained view.</param>
    /// <param name="requireCaptured">Exact source/request binding of verified retained or unpublished new metadata.</param>
    /// <param name="cancellationToken">Original owning operation cancellation.</param>
    /// <returns>The exact original verified capture, or the single newly published capture.</returns>
    NativeCatalogBackupCapture CaptureOrReadCatalogBackup(string directory, Action<IKeyValueView> authorize,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, Action<NativeCatalogBackupCapture> requireCaptured,
        CancellationToken cancellationToken);
}

/// <summary>Owning local capture result; it supplies no database or replica authority.</summary>
/// <param name="Position">Actual store cut copied into the verified archive.</param>
/// <param name="ManifestDigest">SHA-256 of the complete fourth native catalog envelope, including its original inner archive-manifest binding.</param>
/// <param name="Metadata">Exact native bounded catalog metadata captured at that same cut.</param>
public sealed record NativeCatalogBackupCapture(long Position, ReadOnlyMemory<byte> Metadata, string ManifestDigest);
