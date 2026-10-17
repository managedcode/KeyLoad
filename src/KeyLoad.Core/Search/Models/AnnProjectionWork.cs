namespace KeyLoad.Core.Features.Search;

internal sealed record AnnProjectionWork(ProjectionConsumerInfo Consumer, PrincipalRecord Principal,
    Guid NodeId, PhysicalShardRecord Placement, long ThroughSequence);
