namespace KeyLoad;

/// <summary>Identifies a topic or stream within a partition.</summary>
/// <param name="Partition">The partition containing the source.</param>
/// <param name="Resource">The topic or stream resource name.</param>
/// <param name="Kind">The type of event source.</param>
/// <param name="StreamId">The stream identifier when the source is a stream.</param>
/// <param name="Generation">The source generation used to detect replacement.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EventSourceRef)]
public sealed record EventSourceRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] EventSourceKind Kind, [property: Orleans.Id(3)] string? StreamId = null, [property: Orleans.Id(4)] long Generation = EventSourceRef.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}
