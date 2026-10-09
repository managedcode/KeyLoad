namespace KeyLoad;

/// <summary>Actual same-transaction offline restore commit evidence, never an RF3 receipt or token.</summary>
/// <param name="Version">Original operation-owned Version value.</param>
/// <param name="Context">Original operation-owned Context value.</param>
/// <param name="OriginalCut">Original operation-owned OriginalCut value.</param>
/// <param name="MappingsDigest">Original operation-owned MappingsDigest value.</param>
/// <param name="NativeCommitPosition">Original operation-owned NativeCommitPosition value.</param>
/// <param name="Kind">Original operation-owned Kind value.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreSlotCommit.SerializerAlias)]
public sealed record ClusterRestoreSlotCommit(
    [property: Orleans.Id(ClusterRestoreSlotCommit.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreSlotCommit.ContextField)] ClusterRestoreSlotContext Context,
    [property: Orleans.Id(ClusterRestoreSlotCommit.OriginalCutField)] ClusterBackupOwnerCut OriginalCut,
    [property: Orleans.Id(ClusterRestoreSlotCommit.MappingsDigestField)] string MappingsDigest,
    [property: Orleans.Id(ClusterRestoreSlotCommit.NativeCommitPositionField)] long NativeCommitPosition,
    [property: Orleans.Id(ClusterRestoreSlotCommit.KindField)] ClusterRestoreSlotCommitKind Kind)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore.slot-commit.v1";
    /// <summary>Current operation-owned schema version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int ContextField = 1;
    private const int OriginalCutField = 2;
    private const int MappingsDigestField = 3;
    private const int NativeCommitPositionField = 4;
    private const int KindField = 5;
}
