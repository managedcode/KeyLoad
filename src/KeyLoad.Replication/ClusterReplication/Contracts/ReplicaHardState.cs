namespace KeyLoad.Replication;

/// <summary>Persists the authority, election and committed-prefix metadata of one voter.</summary>
/// <param name="Version">Replica metadata format version.</param>
/// <param name="Incarnation">Authority incarnation of the replica group.</param>
/// <param name="Term">Latest durably observed election term.</param>
/// <param name="VotedFor">Voter selected in this term, or null before voting.</param>
/// <param name="LastIndex">Last retained log or checkpoint position.</param>
/// <param name="CommittedIndex">Durably committed majority prefix.</param>
/// <param name="Snapshot">Published verified checkpoint, or null before checkpointing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaHardState)]
public sealed record ReplicaHardState(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Term,
    [property: Orleans.Id(3)] string? VotedFor,
    [property: Orleans.Id(4)] long LastIndex,
    [property: Orleans.Id(5)] long CommittedIndex,
    [property: Orleans.Id(6)] ReplicaSnapshot? Snapshot);
