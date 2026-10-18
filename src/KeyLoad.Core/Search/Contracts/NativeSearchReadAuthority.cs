namespace KeyLoad.Core.Features.Search;

/// <summary>Call-scoped original read authority; no signing key or persistence/transport identity is copied.</summary>
internal readonly record struct NativeSearchReadAuthority(Guid NodeId, Guid Incarnation, int DataEpoch,
    long ReadGeneration, long Position, long AppliedPosition, long PolicyEpoch, long SchemaVersion,
    string ResourceSha256, AtomicPartitionPlacementResolution Placement);
