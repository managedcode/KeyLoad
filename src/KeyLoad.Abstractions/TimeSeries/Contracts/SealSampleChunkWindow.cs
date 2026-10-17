namespace KeyLoad;

/// <summary>Atomically seals the exact enrolled original open window.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Seal)]
public sealed record SealSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);
