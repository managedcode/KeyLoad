namespace KeyLoad.Clustering;

/// <summary>Server-only credential read issued by the original sender.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainContracts.AdministratorAuthenticationRequestAlias)]
internal sealed record AdministratorAuthenticationRequest(
    [property: Orleans.Id(0)] string Secret,
    [property: Orleans.Id(4)] DateTimeOffset ExpiresAt);
