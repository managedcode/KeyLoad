namespace KeyLoad.Core.Features.Search;

internal sealed record NativeTextSeedPin(ProjectionConsumerRef Consumer, long Generation,
    string Collection, string Field, Guid NodeId, PhysicalShardRecord Placement);
