namespace KeyLoad;

/// <summary>Reports the current command admission usage for a node.</summary>
/// <param name="Commands">Number of currently admitted commands.</param>
/// <param name="RetainedBytes">Bytes currently retained by admitted commands.</param>
/// <param name="ControlCommands">Number of currently admitted control commands.</param>
/// <param name="ControlRetainedBytes">Bytes currently retained by control commands.</param>
/// <param name="ActiveTenantScopes">Number of tenants with active admission scopes.</param>
/// <param name="ActivePrincipalScopes">Number of principals with active admission scopes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.CommandAdmissionSnapshot)]
public sealed record CommandAdmissionSnapshot([property: Orleans.Id(0)] int Commands, [property: Orleans.Id(1)] long RetainedBytes, [property: Orleans.Id(2)] int ControlCommands, [property: Orleans.Id(3)] long ControlRetainedBytes,
    [property: Orleans.Id(4)] int ActiveTenantScopes, [property: Orleans.Id(5)] int ActivePrincipalScopes);
