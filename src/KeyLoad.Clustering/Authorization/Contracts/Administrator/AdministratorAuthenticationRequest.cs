namespace KeyLoad.Clustering;

/// <summary>Server-only credential read constrained by the original sender's numeric grant.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainContracts.AdministratorAuthenticationRequestAlias)]
internal sealed record AdministratorAuthenticationRequest(
    [property: Orleans.Id(0)] string Secret,
    [property: Orleans.Id(1)] long MaximumReadBytes,
    [property: Orleans.Id(2)] int MaximumExaminedRecords,
    [property: Orleans.Id(3)] int MaximumResultBytes,
    [property: Orleans.Id(4)] DateTimeOffset ExpiresAt);
