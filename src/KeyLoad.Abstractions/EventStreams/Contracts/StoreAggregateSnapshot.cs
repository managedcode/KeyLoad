namespace KeyLoad;

/// <summary>Stores bounded derived worker state using an exact prior snapshot version.</summary>
/// <param name="StreamSet">The configured event stream resource.</param>
/// <param name="StreamId">The canonical stream identity.</param>
/// <param name="SourceRevision">The final event revision incorporated into state.</param>
/// <param name="ReducerVersion">The exact worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The positive state schema version.</param>
/// <param name="StateJson">The original valid JSON worker state.</param>
/// <param name="ExpectedSnapshotVersion">The prior version, or zero for an absent slot.</param>
/// <param name="Generation">The current stream generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StoreAggregateSnapshot)]
public sealed record StoreAggregateSnapshot([property: Orleans.Id(0)] string StreamSet,
    [property: Orleans.Id(1)] string StreamId, [property: Orleans.Id(2)] long SourceRevision,
    [property: Orleans.Id(3)] string ReducerVersion, [property: Orleans.Id(4)] int StateSchemaVersion,
    [property: Orleans.Id(5)] string StateJson, [property: Orleans.Id(6)] long ExpectedSnapshotVersion,
    [property: Orleans.Id(7)] long Generation = StoreAggregateSnapshot.DefaultGeneration) : Mutation(StreamSet)
{
    private const int DefaultGeneration = 1;
}
