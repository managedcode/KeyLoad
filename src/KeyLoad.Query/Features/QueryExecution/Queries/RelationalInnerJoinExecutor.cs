using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.RelationalStorage;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Query;

/// <summary>Executes the closed bounded Q2 primary-key nested-loop join within one native read view.</summary>
internal static class RelationalInnerJoinExecutor
{
    private const int SingleOrder = 1;
    private const int FirstElementIndex = 0;
    private const int EmptyCount = 0;
    private const int EqualOrder = 0;
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string InvalidJoin = "The bounded relational join is invalid.";
    private const string WorkExceeded = "The bounded relational join exceeds the configured work budget.";

    private sealed record Candidate(DocumentRecord Left, DocumentRecord Right);
    private sealed record CandidateSet(long Cut, SortedSet<Candidate> Rows);

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        internal static CandidateComparer Instance { get; } = new();

        public int Compare(Candidate? left, Candidate? right)
            => StringComparer.Ordinal.Compare(left?.Left.Reference.Id, right?.Left.Reference.Id);
    }

    internal static QueryPage Execute(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition leftResource, ResourceDefinition rightResource, AstQueryRequest request, ReadExecutionBudget budget)
    {
        var query = request.Query;
        var join = query.InnerJoin ?? throw Invalid();
        Validate(database, principal, leftResource, rightResource, request, budget, join);
        var candidates = ReadCandidates(database, view, principal, leftResource, rightResource, request, budget, join);
        return BuildPage(database, principal, leftResource, rightResource, request, budget, join, candidates);
    }

    private static void Validate(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition leftResource, ResourceDefinition rightResource, AstQueryRequest request,
        ReadExecutionBudget budget, InnerJoinClause join)
    {
        var query = request.Query;
        var leftSchema = leftResource.RelationalSchema;
        var rightSchema = rightResource.RelationalSchema;
        if (leftSchema is null || rightSchema is null || query.Alias is null || query.ModelSource is not null || query.Filter is not null || query.Explain
            || request.Cursor is not null || !request.AllowFullScan
            || request.Parameters is { Count: > EmptyCount }
            || StringComparer.Ordinal.Equals(leftResource.Name, rightResource.Name)
            || StringComparer.Ordinal.Equals(query.Alias, join.Alias)
            || query.Order.Length != SingleOrder || query.Order[FirstElementIndex].Descending
            || query.Order[FirstElementIndex].Path != JsonData.Path([leftSchema.PrimaryKey])
            || query.Projection.IsDefaultOrEmpty || query.Projection.Any(selection => selection.Path == SqlSyntax.Star))
        {
            throw Invalid();
        }

        RelationalRowValidation.ValidateSchema(leftResource);
        RelationalRowValidation.ValidateSchema(rightResource);
        var leftColumns = leftSchema.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var rightColumns = rightSchema.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var leftJoinColumn = ColumnName(join.LeftKeyPath);
        var rightJoinColumn = ColumnName(join.RightKeyPath);
        if (!leftColumns.TryGetValue(leftJoinColumn, out var leftKey) || leftKey.Type != RelationalColumnType.Text
            || !rightColumns.TryGetValue(rightJoinColumn, out var rightKey) || rightKey.Type != RelationalColumnType.Text
            || rightSchema.PrimaryKey != rightJoinColumn)
        {
            throw Invalid();
        }
        JsonData.Identifier(query.Alias!);
        ValidateSelections(query.Projection, query.Alias!, join.Alias, leftColumns, rightColumns);
        database.Authorization.RequireFieldUse(principal, leftResource, JsonData.Path([leftJoinColumn]));
        database.Authorization.RequireFieldUse(principal, rightResource, JsonData.Path([rightJoinColumn]));
        database.Authorization.RequireFieldUse(principal, leftResource, JsonData.Path([leftSchema.PrimaryKey]));
        budget.Check();
    }

    private static CandidateSet ReadCandidates(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition leftResource, ResourceDefinition rightResource,
        AstQueryRequest request, ReadExecutionBudget budget, InnerJoinClause join)
    {
        var query = request.Query;
        var leftJoinColumn = ColumnName(join.LeftKeyPath);
        var cut = database.Store.Position;
        var candidates = new SortedSet<Candidate>(CandidateComparer.Instance);
        var work = EmptyCount;
        var scan = budget.VisitRange(view, DocumentStorageKeys.Prefix(request.Partition, leftResource.Name),
            database.Limits.MaxScanRecords, (_, payload) =>
            {
                budget.Check();
                work++;
                if (work > database.Limits.MaxScanRecords)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
                }
                var left = NativeSerialization.Deserialize<DocumentRecord>(payload);
                if (left.Deleted || !database.Authorization.CanReadRow(principal, left.Access))
                {
                    return true;
                }
                using var leftJson = JsonDocument.Parse(left.Json);
                if (!leftJson.RootElement.TryGetProperty(leftJoinColumn, out var leftValue)
                    || leftValue.ValueKind != JsonValueKind.String)
                {
                    return true;
                }
                if (work >= database.Limits.MaxScanRecords)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
                }
                work++;
                budget.Check();
                var right = budget.ReadRecord<DocumentRecord>(view,
                    DocumentStorageKeys.RecordKey(request.Partition, rightResource.Name, leftValue.GetString()!));
                if (right is null || right.Deleted || !database.Authorization.CanReadRow(principal, right.Access))
                {
                    return true;
                }
                Retain(candidates, query.Limit, new(left, right), budget);
                return true;
            });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, WorkExceeded);
        }

        return new(cut, candidates);
    }

    private static QueryPage BuildPage(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition leftResource, ResourceDefinition rightResource, AstQueryRequest request,
        ReadExecutionBudget budget, InnerJoinClause join, CandidateSet candidateSet)
    {
        var rows = ImmutableArray.CreateBuilder<QueryRow>(candidateSet.Rows.Count);
        foreach (var candidate in candidateSet.Rows)
        {
            budget.Check();
            rows.Add(RelationalInnerJoinProjection.Project(database, principal, leftResource, rightResource, request.Query.Alias!, join.Alias,
                candidate.Left, candidate.Right, request.Query.Projection));
        }
        var page = new QueryPage(rows.MoveToImmutable(), null, candidateSet.Cut, AccessPath);
        budget.CheckResult(page);
        return page;
    }

    private static void ValidateSelections(ImmutableArray<Selection> selections, string leftAlias, string rightAlias,
        Dictionary<string, RelationalColumn> leftColumns, Dictionary<string, RelationalColumn> rightColumns)
    {
        foreach (var selection in selections)
        {
            var column = ColumnName(selection.Path);
            if (selection.SourceAlias == leftAlias && leftColumns.ContainsKey(column)
                || selection.SourceAlias == rightAlias && rightColumns.ContainsKey(column))
            {
                continue;
            }
            throw Invalid();
        }
    }

    private static string ColumnName(string path)
    {
        var segments = JsonData.PathSegments(path);
        if (segments.Length != SingleOrder)
        {
            throw Invalid();
        }
        return segments[FirstElementIndex];
    }

    private static void Retain(SortedSet<Candidate> candidates, int limit, Candidate candidate, ReadExecutionBudget budget)
    {
        budget.Check();
        if (candidates.Count < limit)
        {
            candidates.Add(candidate);
            return;
        }
        var worst = candidates.Max ?? throw Invalid();
        if (StringComparer.Ordinal.Compare(candidate.Left.Reference.Id, worst.Left.Reference.Id) >= EqualOrder)
        {
            return;
        }
        budget.Check();
        candidates.Remove(worst);
        candidates.Add(candidate);
    }

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidJoin);
}
