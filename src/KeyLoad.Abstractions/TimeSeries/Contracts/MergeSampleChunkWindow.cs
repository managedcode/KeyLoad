namespace KeyLoad;

/// <summary>Publishes one new immutable generation from exact retained correction records.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Merge)]
public sealed record MergeSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);
