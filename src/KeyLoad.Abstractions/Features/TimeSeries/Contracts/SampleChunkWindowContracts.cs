using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Enrolls a new empty half-open native series window without backfill.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Open)]
public sealed record OpenSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset Until) : Mutation(SeriesSet);

/// <summary>Atomically seals the exact enrolled original open window.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Seal)]
public sealed record SealSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);

/// <summary>Publishes one new immutable generation from exact retained correction records.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Merge)]
public sealed record MergeSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);

/// <summary>Retires representation only after its complete window is below the native retention floor.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Drop)]
public sealed record DropSampleChunkWindow([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] Guid WindowId,
    [property: Orleans.Id(3)] long ExpectedRevision) : Mutation(SeriesSet);

/// <summary>Reads one authorized enrolled window with an optional authorized scalar tag equality.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Read)]
public sealed record ReadSampleChunkWindowRequest([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] Guid WindowId, [property: Orleans.Id(4)] string? TagPointer,
    [property: Orleans.Id(5)] string? TagValue, [property: Orleans.Id(6)] int Limit);

/// <summary>Complete native scope and watermark of one authorized chunk window read cut.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Result)]
public sealed record SampleChunkWindowResult([property: Orleans.Id(0)] Guid WindowId,
    [property: Orleans.Id(1)] DateTimeOffset From, [property: Orleans.Id(2)] DateTimeOffset Until,
    [property: Orleans.Id(3)] long Generation, [property: Orleans.Id(4)] long Revision,
    [property: Orleans.Id(5)] long SourceSequence, [property: Orleans.Id(6)] long? RetentionBeforeUtcTicks,
    [property: Orleans.Id(7)] ImmutableArray<SampleRecord> Records, [property: Orleans.Id(8)] long CutPosition);
