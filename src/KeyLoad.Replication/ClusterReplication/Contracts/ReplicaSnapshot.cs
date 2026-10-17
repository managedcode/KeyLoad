namespace KeyLoad.Replication;

/// <summary>Describes one verified canonical image and its replica-log cut.</summary>
/// <param name="TransferId">Unique identity fencing concurrent image uploads.</param>
/// <param name="Incarnation">Authority incarnation recorded in the image.</param>
/// <param name="Index">Canonical applied and committed replica position.</param>
/// <param name="Term">Term at the represented log position.</param>
/// <param name="Length">Exact encoded image length.</param>
/// <param name="Sha256">Hexadecimal checksum of the complete image.</param>
/// <param name="FileName">Validated image basename derived from its transfer identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.ReplicaSnapshot)]
public sealed record ReplicaSnapshot(
    [property: Orleans.Id(0)] Guid TransferId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Index,
    [property: Orleans.Id(3)] long Term,
    [property: Orleans.Id(4)] long Length,
    [property: Orleans.Id(5)] string Sha256,
    [property: Orleans.Id(6)] string FileName);
