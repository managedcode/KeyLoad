namespace KeyLoad;

/// <summary>Selects native maintenance of already committed canonical vectors.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(AnnProjectionMaintenanceAliases.Profile)]
public sealed record AnnProjectionMaintenanceProfile(
    [property: global::Orleans.Id(0)] string Collection,
    [property: global::Orleans.Id(1)] string Field,
    [property: global::Orleans.Id(2)] VectorSpace Space);

internal static class AnnProjectionMaintenanceAliases
{
    internal const string Profile = "keyload.ann-projection-maintenance-profile.v1";
    internal const string Authority = "keyload.projection-maintenance-authority.v1";
}
