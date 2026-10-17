using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

/// <summary>Local read result; CurrentReadCut never replaces the descriptor's original SourceCut.</summary>
internal sealed record PartitionMoveTransferDataRead(PartitionMoveImageDescriptor OriginalDescriptor,
    ImmutableArray<PartitionMoveImagePage> Pages, long CurrentReadCut);
