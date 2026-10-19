using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Publishes a batch of events to a topic.</summary>
/// <param name="Topic">The topic name.</param>
/// <param name="Events">The events to publish.</param>
/// <param name="Generation">The topic generation expected by the operation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PublishTopic)]
public sealed record PublishTopic([property: Orleans.Id(0)] string Topic, [property: Orleans.Id(1)] ImmutableArray<EventData> Events, [property: Orleans.Id(2)] long Generation = PublishTopic.DefaultGeneration) : Mutation(Topic)
{
    private const int DefaultGeneration = 1;
}
