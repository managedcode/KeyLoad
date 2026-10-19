using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Appends samples and tags to a time-series resource.</summary>
/// <param name="SeriesSet">Identifies the time-series set.</param>
/// <param name="SeriesId">Identifies the time series.</param>
/// <param name="Samples">Lists samples to append.</param>
/// <param name="TagsJson">Contains the sample tags as JSON.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AppendSamples)]
public sealed record AppendSamples([property: Orleans.Id(0)] string SeriesSet, [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] ImmutableArray<SampleData> Samples, [property: Orleans.Id(3)] string TagsJson = AppendSamples.DefaultTagsJson) : Mutation(SeriesSet)
{
    private const string DefaultTagsJson = "{}";
}
