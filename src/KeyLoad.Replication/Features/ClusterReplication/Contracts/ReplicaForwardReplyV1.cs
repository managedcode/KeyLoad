namespace KeyLoad.Replication;

/// <summary>Carries a joined result or counted refusal inside the existing whole authenticated reply.</summary>
[GenerateSerializer, Alias("KeyLoad.Replication.ReplicaForwardReplyV1")]
internal sealed record ReplicaForwardReplyV1(
    [property: Id(0)] int Version,
    [property: Id(1)] ReadOnlyMemory<byte> Result,
    [property: Id(2)] ErrorCode? Error,
    [property: Id(3)] string? SafeDetail,
    [property: Id(4)] ReplicaForwardReadStatsV1? Statistics);
