namespace KeyLoad.Core.Search;

internal sealed record TextSeedCapture(string PrincipalId, long PolicyEpoch,
    long SchemaVersion, long Position, long AppliedPosition, long UpperSequence,
    long FirstAvailableSequence, long Checkpoint, Guid Incarnation, int DataEpoch, long ReadGeneration,
    string ResourceSha256, DocumentRecord[] Documents);
