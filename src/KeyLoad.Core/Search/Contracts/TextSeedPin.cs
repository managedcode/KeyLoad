namespace KeyLoad.Core.Search;

internal sealed record TextSeedPin(ProjectionConsumerRef Consumer, long Generation,
    string Collection, string Field, Guid NodeId, PhysicalShardRecord Placement);
