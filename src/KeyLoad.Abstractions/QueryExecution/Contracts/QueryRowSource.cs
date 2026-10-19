namespace KeyLoad;

/// <summary>Identifies one source row contributing to a joined query result.</summary>
/// <param name="Alias">The declared SQL source alias.</param>
/// <param name="EntityId">The canonical entity identifier.</param>
/// <param name="Revision">The committed source revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.QueryRowSource)]
public sealed record QueryRowSource([property: Orleans.Id(0)] string Alias, [property: Orleans.Id(1)] string EntityId, [property: Orleans.Id(2)] long Revision);
