using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Appends events to a stream under an expected revision and generation.</summary>
/// <param name="StreamSet">Identifies the event stream set.</param>
/// <param name="StreamId">Identifies the event stream.</param>
/// <param name="Events">Lists events to append.</param>
/// <param name="ExpectedRevision">Sets the optional revision precondition.</param>
/// <param name="Generation">Identifies the stream or delivery generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AppendEvents)]
public sealed record AppendEvents([property: Orleans.Id(0)] string StreamSet, [property: Orleans.Id(1)] string StreamId, [property: Orleans.Id(2)] ImmutableArray<EventData> Events,
    [property: Orleans.Id(3)] ExpectedStreamRevision ExpectedRevision, [property: Orleans.Id(4)] long Generation = AppendEvents.DefaultGeneration) : Mutation(StreamSet)
{
    private const int DefaultGeneration = 1;
}
