using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query;

/// <summary>Evaluates the supported Q1 scalar and three-valued predicate semantics.</summary>
public static class PredicateEvaluator
{
    private const string MissingParameter = "A required query parameter is missing.";
    /// <summary>Resolves a query operand against a document and bound parameters.</summary>
    /// <param name="operand">Typed field, literal, or parameter operand.</param>
    /// <param name="document">Document supplying metadata fields.</param>
    /// <param name="json">Parsed document JSON.</param>
    /// <param name="parameters">Optional bound scalar parameters.</param>
    /// <returns>The resolved scalar value or the missing sentinel.</returns>
    public static object? Value(Operand operand, DocumentRecord document, JsonElement json, Dictionary<string, JsonElement>? parameters)
        => Value(operand, document, json, parameters, null);

    internal static object? Value(Operand operand, DocumentRecord document, JsonElement json,
        Dictionary<string, JsonElement>? parameters, IReadOnlyDictionary<string, string[]>? paths)
        => operand switch
        {
            FieldOperand field => FieldValue(field.Path, document, json, paths),
            ValueOperand literal => JsonData.Scalar(literal.Value, ReadOnlySpan<string>.Empty),
            ParameterOperand parameter when parameters?.TryGetValue(parameter.Name, out var value) == true
                => JsonData.Scalar(value, ReadOnlySpan<string>.Empty),
            ParameterOperand => throw Errors.Fail(ErrorCode.Validation, MissingParameter),
            _ => throw Errors.Fail(ErrorCode.Validation, "An operand is invalid.")
        };

    internal static object? FieldValue(string path, DocumentRecord document, JsonElement json,
        IReadOnlyDictionary<string, string[]>? paths) => path switch
        {
            "/@id" => document.Reference.Id,
            "/@revision" => (decimal)document.Revision,
            _ => paths is null ? JsonData.Scalar(json, path) : JsonData.Scalar(json, paths[path])
        };
    /// <summary>Evaluates a typed predicate with SQL-style unknown results.</summary>
    /// <param name="predicate">Typed Q1 predicate.</param>
    /// <param name="document">Document supplying metadata fields.</param>
    /// <param name="json">Parsed document JSON.</param>
    /// <param name="parameters">Optional bound scalar parameters.</param>
    /// <returns>True, false, or unknown.</returns>
    public static bool? Evaluate(Predicate predicate, DocumentRecord document, JsonElement json, Dictionary<string, JsonElement>? parameters)
        => Evaluate(predicate, document, json, parameters, null);

    internal static bool? Evaluate(Predicate predicate, DocumentRecord document, JsonElement json,
        Dictionary<string, JsonElement>? parameters, IReadOnlyDictionary<string, string[]>? paths)
    {
        object? Get(Operand value) => Value(value, document, json, parameters, paths);
        return predicate switch
        {
            Comparison comparison => Compare(Get(comparison.Left), comparison.Operator, Get(comparison.Right)),
            Logical { Operator: "AND" } logical => And(Evaluate(logical.Left, document, json, parameters, paths), Evaluate(logical.Right, document, json, parameters, paths)),
            Logical logical => Or(Evaluate(logical.Left, document, json, parameters, paths), Evaluate(logical.Right, document, json, parameters, paths)),
            Negation negation => !Evaluate(negation.Inner, document, json, parameters, paths),
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
            if (match is true)
            {
                return !negated;
            }
            if (match is null)
            {
                found = null;
            }
        }
        return negated ? !found : found;
    }
    private static bool? And(bool? left, bool? right) => left == false || right == false ? false : left is null || right is null ? null : true;
    private static bool? Or(bool? left, bool? right) => left == true || right == true ? true : left is null || right is null ? null : false;
    /// <summary>Compares two Q1 scalar values with type and null rules.</summary>
    /// <param name="left">Left scalar.</param>
    /// <param name="operation">Supported comparison operator.</param>
    /// <param name="right">Right scalar.</param>
    /// <returns>True, false, or unknown.</returns>
    public static bool? Compare(object? left, string operation, object? right)
    {
        if (left is null or MissingValue || right is null or MissingValue)
        {
            return null;
        }
        if (left.GetType() != right.GetType())
        {
            throw Errors.Fail(ErrorCode.Validation, "Query comparisons require values of the same scalar type.");
        }
        var order = KeyCodec.Encode(left).AsSpan().SequenceCompareTo(KeyCodec.Encode(right));
        return operation switch
        {
            "=" => order == 0,
            "!=" or "<>" => order != 0,
            ">" => order > 0,
            ">=" => order >= 0,
            "<" => order < 0,
            "<=" => order <= 0,
            _ => throw Errors.Fail(ErrorCode.Validation, "The comparison operator is invalid.")
        };
    }
    /// <summary>Enumerates field paths referenced by a predicate.</summary>
    /// <param name="predicate">Predicate to inspect.</param>
    /// <returns>Field paths in traversal order.</returns>
    public static IEnumerable<string> Fields(Predicate? predicate) => predicate switch
    {
        Comparison comparison => Fields(comparison.Left).Concat(Fields(comparison.Right)),
        Logical logical => Fields(logical.Left).Concat(Fields(logical.Right)),
        Negation negation => Fields(negation.Inner),
        NullTest test => Fields(test.Value),
        InPredicate list => Fields(list.Value).Concat(list.Values.SelectMany(Fields)),
        _ => []
    };
    private static IEnumerable<string> Fields(Operand operand) => operand is FieldOperand field ? [field.Path] : [];

    internal static void CheckParameters(Predicate? predicate, Dictionary<string, JsonElement>? parameters)
    {
        void Check(Operand operand)
        {
            if (operand is ParameterOperand parameter && (parameters is null || !parameters.ContainsKey(parameter.Name)))
            {
                throw Errors.Fail(ErrorCode.Validation, MissingParameter);
            }
        }
        switch (predicate)
        {
            case Comparison comparison:
                Check(comparison.Left);
                Check(comparison.Right);
                break;
            case Logical logical:
                CheckParameters(logical.Left, parameters);
                CheckParameters(logical.Right, parameters);
                break;
            case Negation negation:
                CheckParameters(negation.Inner, parameters);
                break;
            case NullTest test:
                Check(test.Value);
                break;
            case InPredicate list:
                Check(list.Value);
                foreach (var operand in list.Values)
                {
                    Check(operand);
                }
                break;
        }
    }
}
