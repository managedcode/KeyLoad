namespace KeyLoad.Query;

/// <summary>Combines two predicates using a logical operator.</summary>
/// <param name="Left">The first predicate.</param>
/// <param name="Operator">The logical operator.</param>
/// <param name="Right">The second predicate.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Logical)]
public sealed record Logical([property: Orleans.Id(0)] Predicate Left, [property: Orleans.Id(1)] string Operator, [property: Orleans.Id(2)] Predicate Right) : Predicate;
