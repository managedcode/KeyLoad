using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

internal sealed record AnnProjectionWork(ProjectionConsumerInfo Consumer, PrincipalRecord Principal,
    Guid NodeId, PhysicalShardRecord Placement, long ThroughSequence);
internal sealed record AnnProjectionWorkPage(ImmutableArray<AnnProjectionWork> Items, byte[]? AfterKey,
    ImmutableArray<ErrorCode> Rejected);
