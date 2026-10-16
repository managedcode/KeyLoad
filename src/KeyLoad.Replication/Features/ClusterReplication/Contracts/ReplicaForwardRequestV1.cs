namespace KeyLoad.Replication;

/// <summary>Wraps exact original operation bytes and an optional finite original-call read grant.</summary>
[GenerateSerializer, Alias("KeyLoad.Replication.ReplicaForwardRequestV1")]
internal sealed record ReplicaForwardRequestV1(
    [property: Id(0)] int Version,
    [property: Id(1)] ReadOnlyMemory<byte> Operation,
    [property: Id(2)] ReplicaForwardReadGrantV1? Grant,
    [property: Id(3)] DateTimeOffset? ExpiresAt);
