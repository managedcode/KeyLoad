using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Borrowed simultaneous native views; never persisted or exposed as request authority.</summary>
internal sealed record ClusterRestoreImageState(IKeyValueView Source, IKeyValueView Target,
    ClusterBackupOwnerCut Original, ImmutableArray<ClusterRestoreOwnerMapping> Mappings,
    ClusterRestoreSlotContext Context, long Position, bool Reset, IOptions<DatabaseLimits> Limits,
    ReadExecutionBudget Work)
{
    internal ClusterRestoreOwnerMapping Local => Mappings.Single(mapping =>
        mapping.Source.PhysicalShardId == Original.Owner.PhysicalShardId);
}
