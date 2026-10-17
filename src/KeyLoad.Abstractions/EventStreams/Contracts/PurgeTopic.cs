namespace KeyLoad;

/// <summary>Removes a bounded inclusive topic history cut after persisted subscription pins permit it.</summary>
/// <param name="Topic">Authorized topic resource.</param>
/// <param name="ThroughPosition">Inclusive final source position to remove.</param>
/// <param name="Generation">Exact source generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(TopicRetentionAliases.PurgeTopic)]
public sealed record PurgeTopic([property: Orleans.Id(0)] string Topic,
    [property: Orleans.Id(1)] long ThroughPosition, [property: Orleans.Id(2)] long Generation = PurgeTopic.DefaultGeneration) : Mutation(Topic)
{
    private const long DefaultGeneration = 1;
}
