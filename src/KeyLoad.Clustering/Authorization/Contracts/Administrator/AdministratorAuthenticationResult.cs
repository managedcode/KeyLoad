namespace KeyLoad.Clustering;

/// <summary>Returns the persisted principal and the actual charged authentication work.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainContracts.AdministratorAuthenticationResultAlias)]
internal sealed record AdministratorAuthenticationResult(
    [property: Orleans.Id(5)] PrincipalRecord? Principal,
    [property: Orleans.Id(3)] ErrorCode? Error,
    [property: Orleans.Id(4)] string? SafeDetail);
