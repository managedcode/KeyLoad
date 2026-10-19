using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Complete execution scope and watermark of one authorized chunk window read cut.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkContractAliases.Result)]
public sealed record SampleChunkWindowResult([property: Orleans.Id(0)] Guid WindowId,
    [property: Orleans.Id(1)] DateTimeOffset From, [property: Orleans.Id(2)] DateTimeOffset Until,
    [property: Orleans.Id(3)] long Generation, [property: Orleans.Id(4)] long Revision,
    [property: Orleans.Id(5)] long SourceSequence, [property: Orleans.Id(6)] long? RetentionBeforeUtcTicks,
    [property: Orleans.Id(7)] ImmutableArray<SampleRecord> Records, [property: Orleans.Id(8)] long CutPosition);
