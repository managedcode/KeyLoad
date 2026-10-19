namespace KeyLoad;

/// <summary>Enrolls a new empty half-open execution series window without backfill.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Open)]
public sealed record OpenSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset Until) : Mutation(SeriesSet);
