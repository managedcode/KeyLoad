namespace KeyLoad.Replication;

/// <summary>Actual persisted store identity and local cut; this contains no cluster authority.</summary>
[GenerateSerializer, Alias(StorageCopyContract.CutAlias)]
public sealed record SiloStorageCut([property: Id(0)] Guid Incarnation,
    [property: Id(1)] long Position);
