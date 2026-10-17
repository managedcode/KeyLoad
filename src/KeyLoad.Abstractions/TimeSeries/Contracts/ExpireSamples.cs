namespace KeyLoad;

/// <summary>Advances an exclusive UTC retention floor and removes a bounded expired page.</summary>
/// <param name="SeriesSet">The configured time-series resource.</param>
/// <param name="SeriesId">The canonical series identity.</param>
/// <param name="Before">Samples strictly before this trusted cutoff become unavailable.</param>
/// <param name="MaximumDeletes">Maximum expired records removed in this atomic command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ExpireSamples)]
public sealed record ExpireSamples([property: Orleans.Id(0)] string SeriesSet,
    [property: Orleans.Id(1)] string SeriesId, [property: Orleans.Id(2)] DateTimeOffset Before,
    [property: Orleans.Id(3)] int MaximumDeletes = SampleRetentionDefaults.DefaultDeletes) : Mutation(SeriesSet);
