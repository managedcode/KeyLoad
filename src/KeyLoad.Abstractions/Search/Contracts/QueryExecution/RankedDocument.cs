namespace KeyLoad;

/// <summary>Pairs a redacted document result with its search score.</summary>
/// <param name="Document">Contains the ranked document result.</param>
/// <param name="Score">Contains the ranking score.</param>
/// <param name="Explanation">Optional bounded contributions from the same authorized ranking branches.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.RankedDocument)]
public sealed record RankedDocument([property: Orleans.Id(0)] DocumentResult Document, [property: Orleans.Id(1)] double Score,
    [property: Orleans.Id(2), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] SearchHitExplanation? Explanation = null);
