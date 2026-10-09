namespace KeyLoad;

/// <summary>Binds one original offline native restore slot; supplies no database request authority.</summary>
/// <param name="Version">Original operation-owned Version value.</param>
/// <param name="OperationId">Original operation-owned OperationId value.</param>
/// <param name="PlanDigest">Original operation-owned PlanDigest value.</param>
/// <param name="SlotOrdinal">Original operation-owned SlotOrdinal value.</param>
/// <param name="SourceEnvelopeDigest">Original operation-owned SourceEnvelopeDigest value.</param>
/// <param name="SourceNodeId">Original operation-owned SourceNodeId value.</param>
/// <param name="SourceIncarnation">Original operation-owned SourceIncarnation value.</param>
/// <param name="SourcePosition">Original operation-owned SourcePosition value.</param>
/// <param name="TargetNodeId">Original operation-owned TargetNodeId value.</param>
/// <param name="TargetIncarnation">Original operation-owned TargetIncarnation value.</param>
/// <param name="TargetSignerFingerprint">Original operation-owned TargetSignerFingerprint value.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreSlotContext.SerializerAlias)]
public sealed record ClusterRestoreSlotContext(
    [property: Orleans.Id(ClusterRestoreSlotContext.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreSlotContext.OperationIdField)] Guid OperationId,
    [property: Orleans.Id(ClusterRestoreSlotContext.PlanDigestField)] string PlanDigest,
    [property: Orleans.Id(ClusterRestoreSlotContext.SlotOrdinalField)] int SlotOrdinal,
    [property: Orleans.Id(ClusterRestoreSlotContext.SourceEnvelopeDigestField)] string SourceEnvelopeDigest,
    [property: Orleans.Id(ClusterRestoreSlotContext.SourceNodeIdField)] Guid SourceNodeId,
    [property: Orleans.Id(ClusterRestoreSlotContext.SourceIncarnationField)] Guid SourceIncarnation,
    [property: Orleans.Id(ClusterRestoreSlotContext.SourcePositionField)] long SourcePosition,
    [property: Orleans.Id(ClusterRestoreSlotContext.TargetNodeIdField)] Guid TargetNodeId,
    [property: Orleans.Id(ClusterRestoreSlotContext.TargetIncarnationField)] Guid TargetIncarnation,
    [property: Orleans.Id(ClusterRestoreSlotContext.TargetSignerFingerprintField)] string TargetSignerFingerprint)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore.slot-context.v1";
    /// <summary>Current operation-owned schema version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int OperationIdField = 1;
    private const int PlanDigestField = 2;
    private const int SlotOrdinalField = 3;
    private const int SourceEnvelopeDigestField = 4;
    private const int SourceNodeIdField = 5;
    private const int SourceIncarnationField = 6;
    private const int SourcePositionField = 7;
    private const int TargetNodeIdField = 8;
    private const int TargetIncarnationField = 9;
    private const int TargetSignerFingerprintField = 10;
}
