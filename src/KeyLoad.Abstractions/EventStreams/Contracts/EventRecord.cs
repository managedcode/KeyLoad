namespace KeyLoad;

/// <summary>Represents one persisted event and its position in a stream.</summary>
/// <param name="Stream">Specifies the stream value.</param>
/// <param name="Revision">Identifies the document, stream, or edge revision.</param>
/// <param name="EventSequence">Identifies the event sequence.</param>
/// <param name="Data">Contains the event data.</param>
/// <param name="RecordedAt">Records the persistence time.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventRecord)]
public sealed record EventRecord([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] long EventSequence, [property: Orleans.Id(3)] EventData Data, [property: Orleans.Id(4)] DateTimeOffset RecordedAt);
