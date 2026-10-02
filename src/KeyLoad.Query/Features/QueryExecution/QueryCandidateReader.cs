using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Streams one authorized access path through the shared read budget.</summary>
internal sealed class QueryCandidateReader(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
    ResourceDefinition resource, AstQueryRequest request, ReadExecutionBudget budget, Action<DocumentRecord> accept)
{
    private const string PointPath = "point";
    private const string IndexPathPrefix = "index:";
    private const string FullScanPath = "bounded-full-scan";
    private const string DocumentSpace = "document";
    private const string IndexSpace = "index";
    private const string IdentifierPath = "/@id";
    private const string CandidateLimitExceeded = "The query access path exceeds its candidate budget.";
    private const string FullScanDenied = "The query requires an index or explicit full-scan permission.";

    internal string Visit()
    {
        var query = request.Query;
        var equality = Equalities(query.Filter, request.Parameters).GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        if (equality.TryGetValue(IdentifierPath, out var identifier) && identifier is string id)
        {
            Read(id);
            return PointPath;
        }
        if (IndexPath(equality) is { } indexed)
        {
            return indexed;
        }
        if (!request.AllowFullScan)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, FullScanDenied);
        }
        var scan = budget.VisitRange(view, KeySpace.Partition(DocumentSpace, request.Partition, query.Collection),
            database.Limits.MaxScanRecords, (_, value) =>
            {
                Candidate(value);
                return true;
            });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, CandidateLimitExceeded);
        }
        return FullScanPath;
    }

    private string? IndexPath(Dictionary<string, object?> equality)
    {
        foreach (var index in resource.Indexes.OrderByDescending(item => item.Fields.Length))
        {
            var prefixFields = index.Fields.TakeWhile(equality.ContainsKey).ToArray();
            if (prefixFields.Length == 0)
            {
                continue;
            }
            var values = prefixFields.Select(field => equality[field]).ToArray();
            if (values.Any(value => value is null or MissingValue))
            {
                continue;
            }
            var prefix = KeySpace.Partition(IndexSpace, request.Partition,
                new object?[] { request.Query.Collection, index.Name }.Concat(values).ToArray());
            var result = budget.VisitRange(view, prefix, database.Limits.MaxScanRecords, (_, value) =>
            {
                Read(JsonDefaults.Deserialize<string>(value));
                return true;
            });
            if (result.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, CandidateLimitExceeded);
            }
            return IndexPathPrefix + index.Name;
        }
        return null;
    }

    private void Candidate(ReadOnlySpan<byte> payload)
    {
        var record = JsonDefaults.Deserialize<DocumentRecord>(payload);
        if (!record.Deleted && database.Authorization.CanReadRow(principal, record.Access))
        {
            accept(record);
        }
    }

    private void Read(string id)
    {
        budget.Check();
        view.ReadValue(KeySpace.Partition(DocumentSpace, request.Partition, request.Query.Collection, id), Candidate,
            budget.ChargeBytes);
    }

    private static IEnumerable<KeyValuePair<string, object?>> Equalities(Predicate? predicate,
        Dictionary<string, JsonElement>? parameters)
    {
        if (predicate is Logical { Operator: SqlSyntax.And } conjunction)
        {
            foreach (var pair in Equalities(conjunction.Left, parameters).Concat(Equalities(conjunction.Right, parameters)))
            {
                yield return pair;
            }
            yield break;
        }
        if (predicate is not Comparison { Operator: SqlSyntax.Equals } comparison)
        {
            yield break;
        }
        var field = comparison.Left as FieldOperand ?? comparison.Right as FieldOperand;
        if (field is null)
        {
            yield break;
        }
        var operand = comparison.Left is FieldOperand ? comparison.Right : comparison.Left;
        if (operand is ValueOperand value)
        {
            yield return new(field.Path, JsonData.Scalar(value.Value, string.Empty));
            yield break;
        }
        if (operand is ParameterOperand parameter
            && parameters?.TryGetValue(parameter.Name, out var literal) == true)
        {
            yield return new(field.Path, JsonData.Scalar(literal, string.Empty));
        }
    }
}
