using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Borrowed original operation owners; this value is never persisted or emitted.</summary>
internal sealed record ClusterRestoreOperationRuntime(
    IOptions<ClusterRestoreOperatorConfiguration> Configuration,
    IOptions<ZoneTreeStorageExecutionOptions> Storage,
    IOptions<DatabaseLimits> Limits,
    TimeProvider Clock,
    ReadExecutionBudget Work,
    ClusterRestorePlan Plan,
    string PlanDigest,
    Dictionary<Guid, byte[]> Signing,
    CancellationToken Cancellation,
    Action<NativeClusterRestoreStage>? Observer = null)
{
    internal ImmutableArray<ClusterBackupOwnerCut> Cuts => Plan.SourceArchives.Select(source => source.OriginalCut).ToImmutableArray();
}
