using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Closed table-owned prefixes; shared provenance and external references remain retained.</summary>
internal static class ResourceRetirementTableCleanupFamilies
{
    internal const int Empty = 0;
    internal const int Step = 1;
    internal const int PrefixComponents = 6;
    internal const int DocumentComponents = 7;
    internal const int VectorComponents = 8;
    internal const int PublicationComponents = 9;
    internal const int UniqueMinimumComponents = 8;
    internal const int IndexMinimumComponents = 9;
    internal const string Invalid = "The table cleanup scope or continuation is invalid.";
    internal const string Capacity = "The table cleanup page exceeds its reserved execution budget.";
    internal const string Accounting = "The table cleanup range accounting is inconsistent.";

    internal static int Count => All.Length;

    internal static ImmutableArray<string> All
    {
        get;
    } =
    [
        PartitionRecordFamilies.Document,
        PartitionRecordFamilies.Index,
        PartitionRecordFamilies.Unique,
        PartitionRecordFamilies.DocumentEpoch,
        PartitionRecordFamilies.VisibilityEpoch,
        PartitionRecordFamilies.Vector,
        PartitionRecordFamilies.VectorProjectionLineage,
        PartitionRecordFamilies.Bm25CorpusCurrent,
        PartitionRecordFamilies.OnlineTextCurrent
    ];
}
