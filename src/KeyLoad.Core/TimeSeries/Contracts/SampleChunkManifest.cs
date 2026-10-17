using System.Collections.Immutable;

namespace KeyLoad.Core.Features.TimeSeries;

[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkLifecycleProtocol.ManifestAlias)]
internal sealed record SampleChunkManifest([property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid WindowId, [property: Orleans.Id(2)] long Generation,
    [property: Orleans.Id(3)] long FromUtcTicks, [property: Orleans.Id(4)] long UntilUtcTicks,
    [property: Orleans.Id(5)] long SourceSequence, [property: Orleans.Id(6)] int RecordCount,
    [property: Orleans.Id(7)] ImmutableArray<ReadOnlyMemory<byte>> BlockDigests,
    [property: Orleans.Id(8)] ImmutableArray<string> TagJsonValues,
    [property: Orleans.Id(9)] long? RetentionBeforeUtcTicks);
