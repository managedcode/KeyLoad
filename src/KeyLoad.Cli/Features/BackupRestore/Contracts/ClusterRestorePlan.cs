using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original operation-owned native restore state; contains no raw secrets or request authority.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestorePlan.SerializerAlias)]
internal sealed record ClusterRestorePlan(
    [property: Orleans.Id(ClusterRestorePlan.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestorePlan.OperationIdField)] Guid OperationId,
    [property: Orleans.Id(ClusterRestorePlan.CaptureIdField)] Guid CaptureId,
    [property: Orleans.Id(ClusterRestorePlan.SourceArchivesField)] ImmutableArray<ClusterRestoreSource> SourceArchives,
    [property: Orleans.Id(ClusterRestorePlan.DestinationPathField)] string DestinationPath,
    [property: Orleans.Id(ClusterRestorePlan.MappingsField)] ImmutableArray<ClusterRestoreOwnerMapping> Mappings,
    [property: Orleans.Id(ClusterRestorePlan.SlotsField)] ImmutableArray<ClusterRestoreSlot> Slots,
    [property: Orleans.Id(ClusterRestorePlan.StoragePolicyField)] ClusterRestoreStoragePolicy StoragePolicy,
    [property: Orleans.Id(ClusterRestorePlan.DatabasePolicyField)] DatabaseLimits DatabasePolicy,
    [property: Orleans.Id(ClusterRestorePlan.OperatorSubjectsField)] ImmutableArray<ClusterRestoreOperatorSubject> OperatorSubjects,
    [property: Orleans.Id(ClusterRestorePlan.CreatedAtUtcField)] DateTimeOffset CreatedAtUtc)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.plan.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int OperationIdField = 1;
    private const int CaptureIdField = 2;
    private const int SourceArchivesField = 3;
    private const int DestinationPathField = 4;
    private const int MappingsField = 5;
    private const int SlotsField = 6;
    private const int StoragePolicyField = 7;
    private const int DatabasePolicyField = 8;
    private const int OperatorSubjectsField = 9;
    private const int CreatedAtUtcField = 12;
}
