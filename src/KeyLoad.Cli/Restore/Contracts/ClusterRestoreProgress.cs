using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreProgress.SerializerAlias)]
internal sealed record ClusterRestoreProgress(
    [property: Orleans.Id(ClusterRestoreProgress.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreProgress.OperationIdField)] Guid OperationId,
    [property: Orleans.Id(ClusterRestoreProgress.PlanDigestField)] string PlanDigest,
    [property: Orleans.Id(ClusterRestoreProgress.RevisionField)] long Revision,
    [property: Orleans.Id(ClusterRestoreProgress.SlotObservationsField)] ImmutableArray<NativeCatalogRestoreSlotCompletion> SlotObservations,
    [property: Orleans.Id(ClusterRestoreProgress.PublicationStateField)] ClusterRestorePublicationState PublicationState,
    [property: Orleans.Id(ClusterRestoreProgress.TerminalReceiptField)] ClusterRestoreExecutionReceipt? TerminalReceipt)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.progress.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int OperationIdField = 1;
    private const int PlanDigestField = 2;
    private const int RevisionField = 3;
    private const int SlotObservationsField = 4;
    private const int PublicationStateField = 5;
    private const int TerminalReceiptField = 6;
}
