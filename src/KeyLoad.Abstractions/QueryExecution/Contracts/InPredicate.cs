using System.Collections.Immutable;

namespace KeyLoad.Query;

/// <summary>Tests whether an operand matches any value in a supplied set.</summary>
/// <param name="Value">The operand to test.</param>
/// <param name="Values">The candidate values.</param>
/// <param name="Negated">Whether to invert the membership test.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.InPredicate)]
public sealed record InPredicate([property: Orleans.Id(0)] Operand Value, [property: Orleans.Id(1)] ImmutableArray<Operand> Values, [property: Orleans.Id(2)] bool Negated) : Predicate;
