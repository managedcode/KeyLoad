using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query;

public sealed record SelectQuery(string Collection, string? Alias, Selection[] Projection, Predicate? Filter,
    Ordering[] Order, int Limit, bool Explain = false);
public sealed record Selection(string Path, string Alias);
public sealed record Ordering(string Path, bool Descending);
public abstract record Operand;
public sealed record FieldOperand(string Path) : Operand;
public sealed record ValueOperand(object? Value) : Operand;
public sealed record ParameterOperand(string Name) : Operand;
public abstract record Predicate;
public sealed record Comparison(Operand Left, string Operator, Operand Right) : Predicate;
public sealed record Logical(Predicate Left, string Operator, Predicate Right) : Predicate;
public sealed record Negation(Predicate Inner) : Predicate;
public sealed record NullTest(Operand Value, bool Negated, bool Missing) : Predicate;
public sealed record InPredicate(Operand Value, Operand[] Values, bool Negated) : Predicate;

public static class PredicateEvaluator
{
    public static object? Value(Operand operand, DocumentRecord document, JsonElement json, Dictionary<string, JsonElement>? parameters)
        => operand switch
        {
            FieldOperand { Path: "/@id" } => document.Reference.Id,
            FieldOperand { Path: "/@revision" } => (decimal)document.Revision,
            FieldOperand field => JsonData.Scalar(json, field.Path),
            ValueOperand literal => literal.Value,
            ParameterOperand parameter when parameters?.TryGetValue(parameter.Name, out var value) == true => JsonData.Scalar(value, ""),
            ParameterOperand => throw Errors.Fail(ErrorCode.Validation, "A required query parameter is missing."),
            _ => throw Errors.Fail(ErrorCode.Validation, "An operand is invalid.")
        };
    public static bool? Evaluate(Predicate predicate, DocumentRecord document, JsonElement json, Dictionary<string, JsonElement>? parameters)
    {
        object? Get(Operand value) => Value(value, document, json, parameters);
        return predicate switch
        {
            Comparison comparison => Compare(Get(comparison.Left), comparison.Operator, Get(comparison.Right)),
            Logical { Operator: "AND" } logical => And(Evaluate(logical.Left, document, json, parameters), Evaluate(logical.Right, document, json, parameters)),
            Logical logical => Or(Evaluate(logical.Left, document, json, parameters), Evaluate(logical.Right, document, json, parameters)),
            Negation negation => !Evaluate(negation.Inner, document, json, parameters),
            NullTest test => (test.Missing ? Get(test.Value) is MissingValue : Get(test.Value) is null) != test.Negated,
            InPredicate list => In(Get(list.Value), list.Values.Select(Get), list.Negated),
            _ => throw Errors.Fail(ErrorCode.Validation, "A predicate is invalid.")
        };
    }
    private static bool? In(object? value, IEnumerable<object?> values, bool negated)
    {
        bool? found = false;
        foreach (var candidate in values)
        {
            var match = Compare(value, "=", candidate);
            if (match is true) return !negated;
            if (match is null) found = null;
        }
        return negated ? !found : found;
    }
    private static bool? And(bool? left, bool? right) => left == false || right == false ? false : left is null || right is null ? null : true;
    private static bool? Or(bool? left, bool? right) => left == true || right == true ? true : left is null || right is null ? null : false;
    public static bool? Compare(object? left, string operation, object? right)
    {
        if (left is null or MissingValue || right is null or MissingValue) return null;
        if (left.GetType() != right.GetType()) throw Errors.Fail(ErrorCode.Validation, "Query comparisons require values of the same scalar type.");
        var order = KeyCodec.Encode(left).AsSpan().SequenceCompareTo(KeyCodec.Encode(right));
        return operation switch { "=" => order == 0, "!=" or "<>" => order != 0, ">" => order > 0, ">=" => order >= 0,
            "<" => order < 0, "<=" => order <= 0, _ => throw Errors.Fail(ErrorCode.Validation, "The comparison operator is invalid.") };
    }
    public static IEnumerable<string> Fields(Predicate? predicate) => predicate switch
    {
        Comparison comparison => Fields(comparison.Left).Concat(Fields(comparison.Right)),
        Logical logical => Fields(logical.Left).Concat(Fields(logical.Right)),
        Negation negation => Fields(negation.Inner), NullTest test => Fields(test.Value),
        InPredicate list => Fields(list.Value).Concat(list.Values.SelectMany(Fields)), _ => []
    };
    private static IEnumerable<string> Fields(Operand operand) => operand is FieldOperand field ? [field.Path] : [];
}
