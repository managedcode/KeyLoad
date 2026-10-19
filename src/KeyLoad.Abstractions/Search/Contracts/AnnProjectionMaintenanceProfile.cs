namespace KeyLoad;

/// <summary>Selects execution maintenance of already committed canonical vectors.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(AnnProjectionMaintenanceAliases.Profile)]
public sealed record AnnProjectionMaintenanceProfile(
    [property: global::Orleans.Id(0)] string Collection,
    [property: global::Orleans.Id(1)] string Field,
    [property: global::Orleans.Id(2)] VectorSpace Space);
