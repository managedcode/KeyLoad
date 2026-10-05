namespace KeyLoad.UnitTests.Features.ClusterRouting.Models;

internal sealed record Interface3ServerSourceProbeResult(string Revision, bool ProofModuleLoaded,
    bool RepositoryWorkspaceIsActual, bool ExtractedWorkspaceHasNoGit, bool SourceMetadataValid,
    bool ProtocolMetadataValid, bool ExportRevalidated, bool RetainedArchiveRevalidated,
    bool RetainedFilesUnchanged, bool SourceCleanupComplete, bool TestRootCleanupComplete,
    string ArchiveName, string InventoryName, int FileCount, long ExpandedBytes, long ArchiveBytes);
