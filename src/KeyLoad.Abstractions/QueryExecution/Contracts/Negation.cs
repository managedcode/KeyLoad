namespace KeyLoad.Query;

/// <summary>Negates the result of a predicate.</summary>
/// <param name="Inner">The predicate whose result is negated.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Negation)]
public sealed record Negation([property: Orleans.Id(0)] Predicate Inner) : Predicate;
