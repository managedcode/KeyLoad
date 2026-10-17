namespace KeyLoad.Query;

/// <summary>Tests whether an operand is null or missing.</summary>
/// <param name="Value">The operand to test.</param>
/// <param name="Negated">Whether to invert the test result.</param>
/// <param name="Missing">Whether the test targets a missing value rather than an explicit null.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.NullTest)]
public sealed record NullTest([property: Orleans.Id(0)] Operand Value, [property: Orleans.Id(1)] bool Negated, [property: Orleans.Id(2)] bool Missing) : Predicate;
