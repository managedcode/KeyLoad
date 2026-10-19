using System.Collections.Immutable;

namespace KeyLoad.Core.Features.TimeSeries;

/// <summary>Reports bounded chunk-work hints without exposing retained series contents.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkWorkPage.SerializerAlias)]
public sealed record SampleChunkWorkPage([property: Orleans.Id(0)] ImmutableArray<SampleChunkWorkHint> Hints,
    [property: Orleans.Id(1)] ImmutableArray<byte>? AfterKey,
    [property: Orleans.Id(2)] bool HasMore)
{
    internal const string SerializerAlias = "keyload.core.sample-chunk-work-page";
}
