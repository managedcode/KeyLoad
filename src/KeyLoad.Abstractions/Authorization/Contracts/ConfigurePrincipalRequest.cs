namespace KeyLoad;

/// <summary>Requests persistence of a principal configuration.</summary>
/// <param name="Principal">Specifies the principal value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ConfigurePrincipalRequest)]
public sealed record ConfigurePrincipalRequest([property: Orleans.Id(0)] PrincipalRecord Principal);
