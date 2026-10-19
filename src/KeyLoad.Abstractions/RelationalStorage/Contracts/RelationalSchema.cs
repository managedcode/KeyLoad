using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Constrains canonical entity rows without introducing another storage engine.</summary>
/// <param name="PrimaryKey">Required nonnullable Text column matching EntityRef.Id.</param>
/// <param name="Columns">Closed immutable top-level column definitions.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.RelationalSchema)]
public sealed record RelationalSchema([property: Orleans.Id(0)] string PrimaryKey, [property: Orleans.Id(1)] ImmutableArray<RelationalColumn> Columns);
