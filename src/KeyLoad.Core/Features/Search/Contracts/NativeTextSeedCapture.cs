namespace KeyLoad.Core.Features.Search;

internal sealed record NativeTextSeedPin(ProjectionConsumerRef Consumer, long Generation,
    string Collection, string Field, Guid NodeId, PhysicalShardRecord Placement);

internal sealed record NativeTextSeedCapture(string PrincipalId, long PolicyEpoch,
    long SchemaVersion, long Position, long AppliedPosition, long UpperSequence,
    long FirstAvailableSequence, long Checkpoint, Guid Incarnation, int DataEpoch, long ReadGeneration,
    string ResourceSha256, DocumentRecord[] Documents);
