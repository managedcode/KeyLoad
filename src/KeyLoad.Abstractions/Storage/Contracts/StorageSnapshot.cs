namespace KeyLoad.Storage;

/// <summary>Identifies the verified store cut represented by a snapshot.</summary>
/// <param name="Incarnation">Storage incarnation recorded in the snapshot.</param>
/// <param name="Position">Local durable storage position of the captured cut.</param>
/// <param name="AppliedPosition">Replicated operation position captured in the cut.</param>
/// <param name="RecordCount">Number of logical records included in the snapshot.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StorageSnapshot)]
public sealed record StorageSnapshot([property: Orleans.Id(0)] Guid Incarnation, [property: Orleans.Id(1)] long Position, [property: Orleans.Id(2)] long AppliedPosition, [property: Orleans.Id(3)] long RecordCount);
