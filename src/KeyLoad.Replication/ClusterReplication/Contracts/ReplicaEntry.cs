namespace KeyLoad.Replication;

/// <summary>Owns an ordered replica entry or current-term establishment noop.</summary>
/// <param name="Index">Monotonic position in the group log.</param>
/// <param name="Term">Election term that created the entry.</param>
/// <param name="Operation">Canonical database operation, or null for a noop.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaEntry)]
public sealed record ReplicaEntry(
    [property: Orleans.Id(0)] long Index,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] ReplicatedOperation? Operation);
