namespace KeyLoad.Query;

/// <summary>Compares two operands using the specified comparison operator.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Right">The right operand.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.Comparison)]
public sealed record Comparison([property: Orleans.Id(0)] Operand Left, [property: Orleans.Id(1)] string Operator, [property: Orleans.Id(2)] Operand Right) : Predicate;
