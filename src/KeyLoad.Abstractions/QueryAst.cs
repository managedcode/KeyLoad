using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Query;

internal static class QueryAstDiscriminatorNames
{
    public const string Kind = "kind";
    public const string Field = "field";
    public const string Value = "value";
    public const string Parameter = "parameter";
    public const string Comparison = "comparison";
    public const string Logical = "logical";
    public const string Not = "not";
    public const string NullTest = "nullTest";
    public const string In = "in";
}

/// <summary>Represents a bounded query plan as a typed abstract syntax tree.</summary>
/// <param name="Collection">The collection to query.</param>
/// <param name="Alias">The optional collection alias.</param>
/// <param name="Projection">The selected fields and aliases.</param>
/// <param name="Filter">The optional predicate applied to rows.</param>
/// <param name="Order">The requested ordering terms.</param>
/// <param name="Limit">The maximum number of rows to return.</param>
/// <param name="Explain">Whether to return query plan information.</param>
public sealed record SelectQuery(string Collection, string? Alias, ImmutableArray<Selection> Projection, Predicate? Filter,
    ImmutableArray<Ordering> Order, int Limit, bool Explain = false);

/// <summary>Selects one field path and assigns it an output alias.</summary>
/// <param name="Path">The field path to select.</param>
/// <param name="Alias">The name used for the selected value in the result.</param>
public sealed record Selection(string Path, string Alias);

/// <summary>Specifies the ordering of query rows by one field path.</summary>
/// <param name="Path">The field path used for ordering.</param>
/// <param name="Descending">Whether to order from greatest to least.</param>
public sealed record Ordering(string Path, bool Descending);

/// <summary>Represents a value or field reference used by a query predicate.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = QueryAstDiscriminatorNames.Kind)]
[JsonDerivedType(typeof(FieldOperand), QueryAstDiscriminatorNames.Field)]
[JsonDerivedType(typeof(ValueOperand), QueryAstDiscriminatorNames.Value)]
[JsonDerivedType(typeof(ParameterOperand), QueryAstDiscriminatorNames.Parameter)]
public abstract record Operand;

/// <summary>Refers to a field path in the current query row.</summary>
/// <param name="Path">The referenced field path.</param>
public sealed record FieldOperand(string Path) : Operand;

/// <summary>Contains a JSON value used as a query operand.</summary>
/// <param name="Value">The serialized value.</param>
[method: JsonConstructor]
public sealed record ValueOperand(JsonElement Value) : Operand
{
    /// <summary>Serializes an object as the operand's JSON value.</summary>
    /// <param name="value">The value to serialize.</param>
    public ValueOperand(object? value) : this(JsonSerializer.SerializeToElement(value, JsonDefaults.Options)) { }
}

/// <summary>Refers to a named parameter supplied with the query request.</summary>
/// <param name="Name">The parameter name.</param>
public sealed record ParameterOperand(string Name) : Operand;

/// <summary>Represents a condition used to filter query rows.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = QueryAstDiscriminatorNames.Kind)]
[JsonDerivedType(typeof(Comparison), QueryAstDiscriminatorNames.Comparison)]
[JsonDerivedType(typeof(Logical), QueryAstDiscriminatorNames.Logical)]
[JsonDerivedType(typeof(Negation), QueryAstDiscriminatorNames.Not)]
[JsonDerivedType(typeof(NullTest), QueryAstDiscriminatorNames.NullTest)]
[JsonDerivedType(typeof(InPredicate), QueryAstDiscriminatorNames.In)]
public abstract record Predicate;

/// <summary>Compares two operands using the specified comparison operator.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="Right">The right operand.</param>
public sealed record Comparison(Operand Left, string Operator, Operand Right) : Predicate;

/// <summary>Combines two predicates using a logical operator.</summary>
/// <param name="Left">The first predicate.</param>
/// <param name="Operator">The logical operator.</param>
/// <param name="Right">The second predicate.</param>
public sealed record Logical(Predicate Left, string Operator, Predicate Right) : Predicate;

/// <summary>Negates the result of a predicate.</summary>
/// <param name="Inner">The predicate whose result is negated.</param>
public sealed record Negation(Predicate Inner) : Predicate;

/// <summary>Tests whether an operand is null or missing.</summary>
/// <param name="Value">The operand to test.</param>
/// <param name="Negated">Whether to invert the test result.</param>
/// <param name="Missing">Whether the test targets a missing value rather than an explicit null.</param>
public sealed record NullTest(Operand Value, bool Negated, bool Missing) : Predicate;

/// <summary>Tests whether an operand matches any value in a supplied set.</summary>
/// <param name="Value">The operand to test.</param>
/// <param name="Values">The candidate values.</param>
/// <param name="Negated">Whether to invert the membership test.</param>
public sealed record InPredicate(Operand Value, ImmutableArray<Operand> Values, bool Negated) : Predicate;

/// <summary>Requests execution of a typed query AST against one partition.</summary>
/// <param name="Partition">The partition to query.</param>
/// <param name="Query">The typed query tree.</param>
/// <param name="Parameters">The named values bound to parameter operands.</param>
/// <param name="AllowFullScan">Whether the caller explicitly permits a full scan.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
/// <param name="AstVersion">The query AST protocol version.</param>
public sealed record AstQueryRequest(PartitionRef Partition, SelectQuery Query, Dictionary<string, JsonElement>? Parameters = null,
    bool AllowFullScan = false, string? Cursor = null, int AstVersion = 1);

/// <summary>Describes supported query capabilities and execution limits.</summary>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="AstVersion">The query AST version.</param>
/// <param name="SqlDialect">The supported SQL dialect name.</param>
/// <param name="Scope">The scope in which the manifest applies.</param>
/// <param name="NumericPolicy">The numeric comparison policy.</param>
/// <param name="MissingPolicy">The policy for missing values.</param>
/// <param name="Adapters">The supported query adapters.</param>
/// <param name="Predicates">The supported predicate forms.</param>
/// <param name="MaxRows">The maximum result row count.</param>
/// <param name="MaxCandidates">The maximum candidate count.</param>
/// <param name="MaxBytes">The maximum result size in bytes.</param>
/// <param name="MaxDepth">The maximum query or expression depth.</param>
/// <param name="FullScanRequiresOptIn">Whether full scans require explicit caller opt-in.</param>
/// <param name="ReadOnly">Whether queries described by this manifest are read-only.</param>
/// <param name="MaxCandidateBytes">The maximum bytes consumed by candidate evaluation.</param>
/// <param name="ReadProfiles">The available read profiles.</param>
public sealed record QueryCapabilityManifest(int ProtocolVersion, int AstVersion, string SqlDialect, string Scope,
    string NumericPolicy, string MissingPolicy, ImmutableArray<string> Adapters, ImmutableArray<string> Predicates, int MaxRows, int MaxCandidates,
    int MaxBytes, int MaxDepth, bool FullScanRequiresOptIn, bool ReadOnly, long MaxCandidateBytes, ImmutableArray<string> ReadProfiles);
