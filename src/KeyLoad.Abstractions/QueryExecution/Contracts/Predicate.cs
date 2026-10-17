using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Represents a condition used to filter query rows.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = QueryAstDiscriminatorNames.Kind)]
[JsonDerivedType(typeof(Comparison), QueryAstDiscriminatorNames.Comparison)]
[JsonDerivedType(typeof(Logical), QueryAstDiscriminatorNames.Logical)]
[JsonDerivedType(typeof(Negation), QueryAstDiscriminatorNames.Not)]
[JsonDerivedType(typeof(NullTest), QueryAstDiscriminatorNames.NullTest)]
[JsonDerivedType(typeof(InPredicate), QueryAstDiscriminatorNames.In)]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.Predicate)]
public abstract record Predicate;
