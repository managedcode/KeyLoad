namespace KeyLoad;

/// <summary>Contains complete raw statistics; empty extrema and average are null.</summary>
/// <param name="Count">Raw sample count, including equal-timestamp samples.</param>
/// <param name="Sum">Finite ordinal sample sum, or zero for an empty range.</param>
/// <param name="Minimum">Minimum sample value, or null when empty.</param>
/// <param name="Maximum">Maximum sample value, or null when empty.</param>
/// <param name="Average">Sum divided by raw Count, or null when empty.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SampleAggregate)]
public sealed record SampleAggregate([property: Orleans.Id(0)] long Count, [property: Orleans.Id(1)] double Sum, [property: Orleans.Id(2)] double? Minimum, [property: Orleans.Id(3)] double? Maximum, [property: Orleans.Id(4)] double? Average);
