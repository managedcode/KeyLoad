using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>Parses the closed Q1.Search.v1 statement into the existing graph-search request.</summary>
internal static class SqlGraphSearchParser
{
    internal static GraphSearchRequest Parse(SqlGraphSearchRequest request, IOptions<DatabaseLimits> limitsOptions, ReadExecutionBudget budget,
        int maximumParameters, int sqlBudgetCheckInterval)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        limits.Validate();
        ValidateEnvelope(request);
        SqlGraphSearchRequestSizer.EnsureBounded(request, limits.MaxQueryBytes, maximumParameters, budget.Cancellation);
        budget.Check();
        var reader = new SqlGraphSearchStatementReader(request.Query, limitsOptions, budget, sqlBudgetCheckInterval);
        return reader.Parse();
    }

    private static void ValidateEnvelope(SqlGraphSearchRequest request)
    {
        if (request.Version != SqlGraphSearchSyntax.Version || request.Query is null
            || request.Query.Partition is null || request.Query.Sql is null
            || request.Query.Cursor is not null || !request.Query.AllowFullScan)
        {
            throw SqlGraphSearchSyntax.Invalid();
        }
    }
}

/// <summary>Consumes the ordered SEARCH grammar while retaining only its bounded typed operators.</summary>
internal sealed class SqlGraphSearchStatementReader
{
    private readonly DatabaseLimits limits;
    private readonly QueryRequest query;
    private readonly SqlGraphSearchValueReader values;
    private readonly SqlTokenCursor cursor;

    internal SqlGraphSearchStatementReader(QueryRequest query, IOptions<DatabaseLimits> limitsOptions, ReadExecutionBudget budget,
        int sqlBudgetCheckInterval)
    {
        ArgumentNullException.ThrowIfNull(limitsOptions);
        limits = limitsOptions.Value;
        limits.Validate();
        this.query = query;
        cursor = new(SqlTokenizer.Lex(query.Sql, limits.MaxQueryTokens, limits.MaxQueryDepth, sqlBudgetCheckInterval, budget));
        values = new(cursor, query, budget);
    }

    internal GraphSearchRequest Parse()
    {
        cursor.Need(SqlGraphSearchSyntax.Search);
        cursor.Need(SqlSyntax.From);
        var collection = cursor.Identifier();
        var text = ReadText();
        var vector = ReadVector();
        var scope = ReadScope();
        var retriever = ReadRetriever();
        var expansion = ReadExpansion();
        var allowed = values.ReadAllowedIds();
        var limit = values.ReadOptionalInt(SqlGraphSearchSyntax.Limit, SqlGraphSearchSyntax.DefaultLimit);
        var fusion = values.ReadOptionalInt(SqlGraphSearchSyntax.Fusion, SqlGraphSearchSyntax.DefaultFusion);
        cursor.Eat(SqlSyntax.Semicolon);
        if (cursor.Current.Kind != SqlTokenKind.End || (scope is null && retriever is null && expansion is null))
        {
            throw SqlGraphSearchSyntax.Invalid();
        }

        var search = new SearchRequest(query.Partition, collection, text.Field, text.Value,
            vector.Field, vector.Values, vector.Space, limit, text.Weight, vector.Weight, fusion, allowed);
        return new(SqlGraphSearchSyntax.Version, search, scope, retriever, expansion);
    }

    private (string? Field, string? Value, double Weight) ReadText()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Text))
        {
            return (null, null, SqlGraphSearchSyntax.DefaultWeight);
        }
        var field = values.ReadField();
        cursor.Need(SqlGraphSearchSyntax.Match);
        var text = values.ReadStringValue();
        var weight = values.ReadOptionalWeight();
        return (field, text, weight);
    }

    private (string? Field, ImmutableArray<float>? Values, VectorSpace? Space, double Weight) ReadVector()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Vector))
        {
            return (null, null, null, SqlGraphSearchSyntax.DefaultWeight);
        }
        var field = values.ReadField();
        cursor.Need(SqlGraphSearchSyntax.Match);
        var vectorValues = values.ReadVectorParameter();
        cursor.Need(SqlGraphSearchSyntax.Space);
        var space = values.ReadSpace();
        var weight = values.ReadOptionalWeight();
        if (vectorValues.Length != space.Dimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
        }
        return (field, vectorValues, space, weight);
    }

    private GraphScope? ReadScope()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Scope))
        {
            return null;
        }
        cursor.Need(SqlGraphSearchSyntax.Graph);
        var graph = cursor.Identifier();
        var walk = ReadWalk(graph);
        return new(walk);
    }

    private GraphRetriever? ReadRetriever()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Retrieve))
        {
            return null;
        }
        cursor.Need(SqlGraphSearchSyntax.Graph);
        var graph = cursor.Identifier();
        var walk = ReadWalk(graph);
        return new(walk, values.ReadOptionalWeight());
    }

    private GraphExpansion? ReadExpansion()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Expand))
        {
            return null;
        }
        cursor.Need(SqlGraphSearchSyntax.Graph);
        var graph = cursor.Identifier();
        var bounds = ReadWalkBounds();
        return new(graph, bounds.Depth, bounds.Vertices, bounds.Edges, values.ReadOptionalLabels());
    }

    private GraphWalkSpec ReadWalk(string graph)
    {
        cursor.Need(SqlGraphSearchSyntax.Seeds);
        var seeds = values.ReadSeeds();
        var bounds = ReadWalkBounds();
        return new(graph, seeds, bounds.Depth, bounds.Vertices, bounds.Edges, values.ReadOptionalLabels());
    }

    private (int Depth, int Vertices, int Edges) ReadWalkBounds()
    {
        cursor.Need(SqlGraphSearchSyntax.Depth);
        var depth = values.ReadInteger();
        cursor.Need(SqlGraphSearchSyntax.Vertices);
        var vertices = values.ReadInteger();
        cursor.Need(SqlGraphSearchSyntax.Edges);
        var edges = values.ReadInteger();
        return (depth, vertices, edges);
    }

}
