namespace KeyLoad.Query;

/// <summary>Defines one admitted right collection and exact equality key pair for a bounded inner join.</summary>
/// <param name="Collection">The right typed collection name.</param>
/// <param name="Alias">The right source alias.</param>
/// <param name="LeftKeyPath">The left typed key column.</param>
/// <param name="RightKeyPath">The right typed primary-key column.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.InnerJoinClause)]
public sealed record InnerJoinClause([property: Orleans.Id(0)] string Collection, [property: Orleans.Id(1)] string Alias,
    [property: Orleans.Id(2)] string LeftKeyPath, [property: Orleans.Id(3)] string RightKeyPath);
