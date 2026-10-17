namespace KeyLoad;

/// <summary>Original joined native slot observation requiring independent current target verification before publication.</summary>
/// <param name="Version">Original operation-owned Version value.</param>
/// <param name="Context">Original operation-owned Context value.</param>
/// <param name="TargetNodeId">Original operation-owned TargetNodeId value.</param>
/// <param name="TargetIncarnation">Original operation-owned TargetIncarnation value.</param>
/// <param name="TargetSignerFingerprint">Original operation-owned TargetSignerFingerprint value.</param>
/// <param name="ReconciledCommitPosition">Original operation-owned ReconciledCommitPosition value.</param>
/// <param name="AuthorityResetCommitPosition">Original operation-owned AuthorityResetCommitPosition value.</param>
/// <param name="ActualFinalStorePosition">Original operation-owned ActualFinalStorePosition value.</param>
[Orleans.GenerateSerializer, Orleans.Alias(NativeCatalogRestoreSlotCompletion.SerializerAlias)]
public sealed record NativeCatalogRestoreSlotCompletion(
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.VersionField)] int Version,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.ContextField)] ClusterRestoreSlotContext Context,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.TargetNodeIdField)] Guid TargetNodeId,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.TargetIncarnationField)] Guid TargetIncarnation,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.TargetSignerFingerprintField)] string TargetSignerFingerprint,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.ReconciledCommitPositionField)] long ReconciledCommitPosition,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.AuthorityResetCommitPositionField)] long AuthorityResetCommitPosition,
    [property: Orleans.Id(NativeCatalogRestoreSlotCompletion.ActualFinalStorePositionField)] long ActualFinalStorePosition)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore.completion.v1";
    /// <summary>Current operation-owned schema version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int ContextField = 1;
    private const int TargetNodeIdField = 2;
    private const int TargetIncarnationField = 3;
    private const int TargetSignerFingerprintField = 4;
    private const int ReconciledCommitPositionField = 5;
    private const int AuthorityResetCommitPositionField = 6;
    private const int ActualFinalStorePositionField = 7;
}
