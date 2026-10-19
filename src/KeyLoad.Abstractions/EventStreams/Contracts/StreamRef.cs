namespace KeyLoad;

/// <summary>Identifies one generation of an event stream.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StreamRef)]
public sealed record StreamRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string StreamSet, [property: Orleans.Id(2)] string StreamId, [property: Orleans.Id(3)] long Generation = StreamRef.DefaultGeneration)
{
    private const int DefaultGeneration = 1;
}
