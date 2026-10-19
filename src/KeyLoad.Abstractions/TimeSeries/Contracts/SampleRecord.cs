namespace KeyLoad;

/// <summary>Represents a stored time-series sample and its sequence and tags.</summary>
/// <param name="SeriesId">Identifies the time series.</param>
/// <param name="Sample">Specifies the sample value.</param>
/// <param name="Sequence">Specifies the sequence value.</param>
/// <param name="TagsJson">Contains the sample tags as JSON.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SampleRecord)]
public sealed record SampleRecord([property: Orleans.Id(0)] string SeriesId, [property: Orleans.Id(1)] SampleData Sample, [property: Orleans.Id(2)] long Sequence, [property: Orleans.Id(3)] string TagsJson);
