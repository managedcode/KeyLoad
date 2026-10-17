namespace KeyLoad.Replication;

/// <summary>Identifies a materialized current-term majority read cut.</summary>
/// <param name="Incarnation">Replica authority that owns this receipt.</param>
/// <param name="Position">Committed and locally materialized read position.</param>
/// <param name="Term">Term in which the majority barrier was established.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReadBarrierReceipt)]
public sealed record ReadBarrierReceipt(
    [property: Orleans.Id(0)] Guid Incarnation,
    [property: Orleans.Id(1)] long Position,
    [property: Orleans.Id(2)] long Term);
