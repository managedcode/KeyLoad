namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreSlot.SerializerAlias)]
internal sealed record ClusterRestoreSlot(
    [property: Orleans.Id(ClusterRestoreSlot.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreSlot.SlotOrdinalField)] int SlotOrdinal,
    [property: Orleans.Id(ClusterRestoreSlot.SourceOwnerIdField)] Guid SourceOwnerId,
    [property: Orleans.Id(ClusterRestoreSlot.TargetOwnerIdField)] Guid TargetOwnerId,
    [property: Orleans.Id(ClusterRestoreSlot.TargetNodeIdField)] Guid TargetNodeId,
    [property: Orleans.Id(ClusterRestoreSlot.TargetIncarnationField)] Guid TargetIncarnation,
    [property: Orleans.Id(ClusterRestoreSlot.TargetSignerFingerprintField)] string TargetSignerFingerprint,
    [property: Orleans.Id(ClusterRestoreSlot.StageNameField)] string StageName)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.slot.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int SlotOrdinalField = 1;
    private const int SourceOwnerIdField = 2;
    private const int TargetOwnerIdField = 3;
    private const int TargetNodeIdField = 4;
    private const int TargetIncarnationField = 5;
    private const int TargetSignerFingerprintField = 6;
    private const int StageNameField = 7;
}
