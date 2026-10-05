using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Compiles only the frozen Q1.GraphPath.v1 expression into the canonical Core request.</summary>
internal static class SqlGraphPathParser
{
    internal static GraphShortestPathRequest Parse(SqlGraphPathRequest request, DatabaseLimits limits,
        ReadExecutionBudget budget, int maximumParameters, int maximumLabels)
    {
        ValidateEnvelope(request);
        SqlGraphPathRequestSizer.EnsureBounded(request, limits.MaxQueryBytes, maximumParameters, budget);
        budget.Check();
        return new SqlGraphPathStatementReader(request.Query, limits, budget, maximumLabels).Parse();
    }

    private static void ValidateEnvelope(SqlGraphPathRequest request)
    {
        if (request is null || request.Version != SqlGraphPathSyntax.Version || request.Query is null
            || request.Query.Partition is null || request.Query.Partition.TenantId is null
            || request.Query.Partition.DatabaseId is null || request.Query.Partition.TransactionDomainId is null
            || request.Query.Partition.PartitionKey is null || request.Query.Sql is null
            || request.Query.Cursor is not null || !request.Query.AllowFullScan)
        {
            throw SqlGraphPathSyntax.Invalid();
        }
    }
}

/// <summary>Consumes the fixed function argument list and rejects every alternate SQL shape.</summary>
internal sealed class SqlGraphPathStatementReader
{
    private readonly QueryRequest query;
    private readonly SqlGraphPathValueReader values;
    private readonly SqlTokenCursor cursor;

    internal SqlGraphPathStatementReader(QueryRequest query, DatabaseLimits limits, ReadExecutionBudget budget,
        int maximumLabels)
    {
        this.query = query;
        cursor = new(SqlTokenizer.Lex(query.Sql, limits.MaxQueryTokens, limits.MaxQueryDepth, budget));
        values = new(cursor, query, budget, maximumLabels);
    }

    internal GraphShortestPathRequest Parse()
    {
        cursor.Need(SqlSyntax.Select);
        cursor.Need(SqlSyntax.Star);
        cursor.Need(SqlSyntax.From);
        cursor.Need(SqlGraphPathSyntax.Function);
        cursor.Need(SqlSyntax.OpenParen);
        var graph = values.ReadString();
        cursor.Need(SqlSyntax.Comma);
        var fromCollection = values.ReadString();
        cursor.Need(SqlSyntax.Comma);
        var fromId = values.ReadString();
        cursor.Need(SqlSyntax.Comma);
        var toCollection = values.ReadString();
        cursor.Need(SqlSyntax.Comma);
        var toId = values.ReadString();
        cursor.Need(SqlSyntax.Comma);
        var depth = values.ReadInteger();
        cursor.Need(SqlSyntax.Comma);
        var vertices = values.ReadInteger();
        cursor.Need(SqlSyntax.Comma);
        var edges = values.ReadInteger();
        var labels = values.ReadOptionalLabels();
        cursor.Need(SqlSyntax.CloseParen);
        cursor.Eat(SqlSyntax.Semicolon);
        if (cursor.Current.Kind != SqlTokenKind.End)
        {
            throw SqlGraphPathSyntax.Invalid();
        }
        return new(SqlGraphPathSyntax.Version, query.Partition,
            graph, new(query.Partition, fromCollection, fromId), new(query.Partition, toCollection, toId),
            depth, vertices, edges, labels);
    }
}
