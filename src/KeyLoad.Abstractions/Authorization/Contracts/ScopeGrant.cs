namespace KeyLoad;

/// <summary>Grants capabilities for a database and resource scope.</summary>
/// <param name="Database">Identifies the database scope.</param>
/// <param name="Resource">Identifies the resource scope or mutation target.</param>
/// <param name="Capabilities">Lists the granted operations.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ScopeGrant)]
public sealed record ScopeGrant([property: Orleans.Id(0)] string Database, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] Capability Capabilities);
