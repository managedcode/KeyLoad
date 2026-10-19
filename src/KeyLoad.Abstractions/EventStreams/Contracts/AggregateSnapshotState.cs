namespace KeyLoad;

/// <summary>Contains an exact-versioned, checksummed latest aggregate snapshot.</summary>
/// <param name="Stream">The complete stream generation.</param>
/// <param name="SnapshotVersion">The monotone CAS version.</param>
/// <param name="SourceRevision">The final incorporated event revision.</param>
/// <param name="ReducerVersion">The exact worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The positive state schema version.</param>
/// <param name="StateJson">The original valid JSON worker state.</param>
/// <param name="Checksum">SHA256 of the frozen snapshot identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AggregateSnapshotState)]
public sealed record AggregateSnapshotState([property: Orleans.Id(0)] StreamRef Stream,
    [property: Orleans.Id(1)] long SnapshotVersion, [property: Orleans.Id(2)] long SourceRevision,
    [property: Orleans.Id(3)] string ReducerVersion, [property: Orleans.Id(4)] int StateSchemaVersion,
    [property: Orleans.Id(5)] string StateJson, [property: Orleans.Id(6)] string Checksum);
