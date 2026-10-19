namespace KeyLoad;

/// <summary>Represents one recorded event from a topic or stream.</summary>
/// <param name="Source">The event source that recorded the event.</param>
/// <param name="Position">The source position of the event.</param>
/// <param name="EventSequence">The sequence number within the source transaction.</param>
/// <param name="Data">The event payload and metadata.</param>
/// <param name="RecordedAt">The time at which the event was recorded.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SourceEventRecord)]
public sealed record SourceEventRecord([property: Orleans.Id(0)] EventSourceRef Source, [property: Orleans.Id(1)] long Position, [property: Orleans.Id(2)] long EventSequence, [property: Orleans.Id(3)] EventData Data, [property: Orleans.Id(4)] DateTimeOffset RecordedAt);
