namespace KeyLoad;

/// <summary>Requests one explicitly versioned vector-only search against a provisioned native generation.</summary>
/// <param name="Version">The current page contract version.</param>
/// <param name="Search">The same authorized vector scope and result bounds as exact search.</param>
/// <param name="Consumer">The provisioned generation consumer; this identifier grants no authority.</param>
/// <param name="RequestedMode">Explicit approximate intent; actual completed mode is reported separately.</param>
/// <param name="IndexGeneration">The exact provisioned native generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AnnSearchContractAliases.Request)]
public sealed record ApproximateSearchRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] SearchRequest Search,
    [property: Orleans.Id(2)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(3)] long IndexGeneration,
    [property: Orleans.Id(4)] AnnPageMode RequestedMode = AnnPageMode.Approximate);
