using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Selects a bounded physical access path without visiting document or index records.</summary>
internal static class QueryAccessPlanner
{
    private const string PointPath = "point";
    private const string PrimaryRangePath = "primary-key-range";
    private const string IndexPathPrefix = "index:";
    private const string FullScanPath = "bounded-full-scan";
    private const string DocumentSpace = "document";
    private const string IndexSpace = "index";
    private const string FullScanDenied = "The query requires an index or explicit full-scan permission.";

    internal static QueryAccessPlan Create(ResourceDefinition resource, AstQueryRequest request)
    {
        var equality = Equalities(request.Query.Filter, request.Parameters).GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        if (equality.TryGetValue(SqlSyntax.MetadataIdPath, out var identifier) && identifier is { Kind: KeyComponentKind.Text, Text: var id })
        {
            return new(QueryAccessKind.Point, PointPath, [], PointId: id);
        }
        foreach (var index in resource.Indexes.OrderByDescending(item => item.Fields.Length))
        {
            var fields = index.Fields.TakeWhile(field => equality.ContainsKey(field)
                && field is not SqlSyntax.MetadataIdPath and not SqlSyntax.MetadataRevisionPath).ToArray();
            if (fields.Length == 0 || fields.Length < index.Fields.Length && (!index.IncludeNull || !index.IncludeMissing))
            {
                continue;
            }
            var values = fields.Select(field => equality[field]).ToArray();
            if (values.Any(value => value.Kind is KeyComponentKind.Null or KeyComponentKind.Missing))
            {
                continue;
            }
            var prefix = KeySpace.Partition(IndexSpace, request.Partition,
                [request.Query.Collection, index.Name, .. values]);
            return new(QueryAccessKind.IndexPrefix, IndexPathPrefix + index.Name, prefix, index.Name, fields);
        }
        foreach (var index in resource.Indexes)
        {
            if (QueryIndexRangePlanner.Create(index, request).Value is { } range)
            {
                return new(QueryAccessKind.IndexRange, IndexPathPrefix + index.Name, range.Prefix,
                    index.Name, [.. index.Fields], AfterKey: range.AfterKey, UntilKey: range.UntilKey);
            }
        }
        if (QueryIndexRangePlanner.CreatePrimary(request).Value is { } primary)
        {
            return new(QueryAccessKind.PrimaryRange, PrimaryRangePath, primary.Prefix,
                IndexFields: [SqlSyntax.MetadataIdPath], AfterKey: primary.AfterKey, UntilKey: primary.UntilKey);
        }
        if (!request.AllowFullScan)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, FullScanDenied);
        }
        return new(QueryAccessKind.FullScan, FullScanPath,
            KeySpace.Partition(DocumentSpace, request.Partition, request.Query.Collection));
    }

    private static IEnumerable<KeyValuePair<string, KeyComponent>> Equalities(Predicate? predicate,
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
        if (operand is ParameterOperand parameter && parameters?.TryGetValue(parameter.Name, out var literal) == true)
        {
            yield return new(field.Path, JsonData.Scalar(literal, string.Empty));
        }
    }
}
