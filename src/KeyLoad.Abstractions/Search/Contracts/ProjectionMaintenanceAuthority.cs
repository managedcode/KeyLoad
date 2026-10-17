namespace KeyLoad;

/// <summary>Retains the authenticated original configuration authority, never caller roles.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(AnnProjectionMaintenanceAliases.Authority)]
public sealed record ProjectionMaintenanceAuthority(
    [property: global::Orleans.Id(0)] string PrincipalId,
    [property: global::Orleans.Id(1)] Guid ConfigureCommandId,
    [property: global::Orleans.Id(2)] Guid Incarnation,
    [property: global::Orleans.Id(3)] long PolicyEpoch);
