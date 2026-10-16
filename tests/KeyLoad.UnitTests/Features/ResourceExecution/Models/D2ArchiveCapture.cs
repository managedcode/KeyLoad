using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ArchiveCapture(
    D2ArchiveSource Source,
    string Directory,
    NativeCatalogBackupCapture Native,
    ClusterBackupOwnerCut Cut,
    string[] Files,
    ControlledPartitionMovementExpiryOwnerState SourceCut);
