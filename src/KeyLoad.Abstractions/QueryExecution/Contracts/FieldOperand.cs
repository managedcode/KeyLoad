namespace KeyLoad.Query;

/// <summary>Refers to a field path in the current query row.</summary>
/// <param name="Path">The referenced field path.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.FieldOperand)]
public sealed record FieldOperand([property: Orleans.Id(0)] string Path) : Operand;
