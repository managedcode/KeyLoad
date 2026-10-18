namespace KeyLoad.Clustering;

/// <summary>Identifies an actual silo generation and its persisted database without granting cluster authority.</summary>
[GenerateSerializer]
public sealed record SiloStorageIdentity([property: Id(0)] string SiloName,
    [property: Id(1)] string ClusterId, [property: Id(2)] Guid Incarnation,
    [property: Id(3)] string SiloAddress);
