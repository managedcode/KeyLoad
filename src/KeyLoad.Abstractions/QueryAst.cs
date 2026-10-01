using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Query;

public sealed record SelectQuery(string Collection, string? Alias, Selection[] Projection, Predicate? Filter,
    Ordering[] Order, int Limit, bool Explain = false);
public sealed record Selection(string Path, string Alias);
public sealed record Ordering(string Path, bool Descending);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FieldOperand), "field")]
[JsonDerivedType(typeof(ValueOperand), "value")]
[JsonDerivedType(typeof(ParameterOperand), "parameter")]
public abstract record Operand;
public sealed record FieldOperand(string Path) : Operand;
[method: JsonConstructor]
public sealed record ValueOperand(JsonElement Value) : Operand
{
    public ValueOperand(object? value) : this(JsonSerializer.SerializeToElement(value, JsonDefaults.Options)) { }
}
public sealed record ParameterOperand(string Name) : Operand;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Comparison), "comparison")]
[JsonDerivedType(typeof(Logical), "logical")]
[JsonDerivedType(typeof(Negation), "not")]
[JsonDerivedType(typeof(NullTest), "nullTest")]
[JsonDerivedType(typeof(InPredicate), "in")]
public abstract record Predicate;
public sealed record Comparison(Operand Left, string Operator, Operand Right) : Predicate;
public sealed record Logical(Predicate Left, string Operator, Predicate Right) : Predicate;
public sealed record Negation(Predicate Inner) : Predicate;
public sealed record NullTest(Operand Value, bool Negated, bool Missing) : Predicate;
public sealed record InPredicate(Operand Value, Operand[] Values, bool Negated) : Predicate;

public sealed record AstQueryRequest(PartitionRef Partition, SelectQuery Query, Dictionary<string, JsonElement>? Parameters = null,
    bool AllowFullScan = false, string? Cursor = null, int AstVersion = 1);
public sealed record QueryCapabilityManifest(int ProtocolVersion, int AstVersion, string SqlDialect, string Scope,
    string NumericPolicy, string MissingPolicy, string[] Adapters, string[] Predicates, int MaxRows, int MaxCandidates,
    int MaxBytes, int MaxDepth, bool FullScanRequiresOptIn, bool ReadOnly);
