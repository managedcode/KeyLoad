using System.Collections.Immutable;

namespace KeyLoad.Core.Features.TimeSeries;

[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkLifecycleProtocol.WindowAlias)]
internal sealed record SampleChunkWindow([property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid WindowId, [property: Orleans.Id(2)] long FromUtcTicks,
    [property: Orleans.Id(3)] long UntilUtcTicks, [property: Orleans.Id(4)] long Revision,
    [property: Orleans.Id(5)] long Generation, [property: Orleans.Id(6)] SampleChunkWindowState State,
    [property: Orleans.Id(7)] string CreatorPrincipalId, [property: Orleans.Id(8)] long SourceSequence,
    [property: Orleans.Id(9)] ImmutableArray<SampleRecord> OpenRecords,
    [property: Orleans.Id(10)] ImmutableArray<long> CorrectionSequences,
    [property: Orleans.Id(11)] Guid MaintenanceCommandId,
    [property: Orleans.Id(12)] long? RetentionBeforeUtcTicks);

internal enum SampleChunkWindowState { Open = 0, Sealed = 1, Dropped = 2 }
