namespace KeyLoad;

/// <summary>Retires representation only after its complete window is below the execution retention floor.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Drop)]
public sealed record DropSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);
