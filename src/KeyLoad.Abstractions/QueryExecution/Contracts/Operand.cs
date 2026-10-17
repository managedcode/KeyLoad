using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Represents a value or field reference used by a query predicate.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = QueryAstDiscriminatorNames.Kind)]
[JsonDerivedType(typeof(FieldOperand), QueryAstDiscriminatorNames.Field)]
[JsonDerivedType(typeof(ValueOperand), QueryAstDiscriminatorNames.Value)]
[JsonDerivedType(typeof(ParameterOperand), QueryAstDiscriminatorNames.Parameter)]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Operand)]
public abstract record Operand;
