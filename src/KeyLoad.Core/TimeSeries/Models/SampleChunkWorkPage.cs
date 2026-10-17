using System.Collections.Immutable;

namespace KeyLoad.Core.Features.TimeSeries;

internal sealed record SampleChunkWorkPage(ImmutableArray<SampleChunkWorkHint> Hints,
    byte[]? AfterKey, bool HasMore);
