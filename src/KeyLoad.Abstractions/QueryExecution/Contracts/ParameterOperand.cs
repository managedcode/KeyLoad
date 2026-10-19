namespace KeyLoad.Query;

/// <summary>Refers to a named parameter supplied with the query request.</summary>
/// <param name="Name">The parameter name.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ParameterOperand)]
public sealed record ParameterOperand([property: Orleans.Id(0)] string Name) : Operand;
