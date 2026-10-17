namespace KeyLoad;

/// <summary>Owning local capture result; it supplies no database or replica authority.</summary>
/// <param name="Position">Actual store cut copied into the verified archive.</param>
/// <param name="ManifestDigest">SHA-256 of the complete fourth native catalog envelope, including its original inner archive-manifest binding.</param>
/// <param name="Metadata">Exact native bounded catalog metadata captured at that same cut.</param>
public sealed record NativeCatalogBackupCapture(long Position, ReadOnlyMemory<byte> Metadata, string ManifestDigest);
