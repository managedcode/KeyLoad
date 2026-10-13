using System.Text;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>A bounded Q1/Q2 parser. Unsupported syntax is rejected, never passed through to storage.</summary>
public sealed class SqlParser
{
    private const int MinimumPositiveCount = 1;
    private const int DefaultDialectVersion = 1;
    private const int JoinDialectVersion = 2;

    private readonly SqlTokenCursor cursor;
    private readonly DatabaseLimits limits;
    private readonly QueryExecutionOptions execution;
    private readonly IOptions<DatabaseLimits> operationLimitsOptions;
    private readonly int dialectVersion;
    private string? alias;

    /// <summary>Creates a parser with the configured SQL byte and token limits.</summary>
    /// <param name="sql">SQL text in the selected bounded query dialect.</param>
    /// <param name="limitsOptions">Centrally validated database query limits.</param>
    /// <param name="executionOptions">Centrally validated query structure limits.</param>
    public SqlParser(string sql, IOptions<DatabaseLimits> limitsOptions, IOptions<QueryExecutionOptions> executionOptions)
        : this(sql, limitsOptions, executionOptions, null, DefaultDialectVersion)
    {
    }

    internal SqlParser(string sql, IOptions<DatabaseLimits> limitsOptions, IOptions<QueryExecutionOptions> executionOptions,
        ReadExecutionBudget? budget, int dialectVersion = DefaultDialectVersion)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        ArgumentNullException.ThrowIfNull(executionOptions);
        limits = limitsOptions.Value;
        limits.Validate();
        execution = executionOptions.Value;
        execution.Validate();
        operationLimitsOptions = limitsOptions;
        if (dialectVersion is not (DefaultDialectVersion or JoinDialectVersion))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedDialectDetail);
        }
        this.dialectVersion = dialectVersion;
        if (sql.Length > limits.MaxQueryBytes || Encoding.UTF8.GetByteCount(sql) > limits.MaxQueryBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.ByteBudgetDetail);
        }
        cursor = new(SqlTokenizer.Lex(sql, limits.MaxQueryTokens, limits.MaxQueryDepth, execution.SqlBudgetCheckInterval, budget));
    }

    /// <summary>Parses the SQL text into a typed, bounded query.</summary>
    /// <returns>A select query with immutable projection and ordering.</returns>
    public SelectQuery Parse()
    {
        var explain = cursor.Eat(SqlSyntax.Explain);
        if (cursor.Eat(SqlSyntax.Table))
        { return ParseTable(explain); }
        cursor.Need(SqlSyntax.Select);
        cursor.Eat(SqlSyntax.All);
        if (cursor.Is(SqlSyntax.All))
        { throw SqlSyntax.Invalid(); }
        var projection = ReadProjection();
        cursor.Need(SqlSyntax.From);
        var (collection, modelSource) = new ModelSqlSourceParser(cursor).Read();
        ReadAlias();
        var joinSyntax = new SqlJoinSyntax(cursor, dialectVersion);
        var join = joinSyntax.ReadJoin(alias, modelSource);
        if (join is not null && StringComparer.Ordinal.Equals(join.Collection, collection))
        {
            throw SqlSyntax.Invalid();
        }
        projection = projection.Select(p => join is null
            ? p with { Path = SqlJoinSyntax.BoundPath(p.Path, alias), SourceAlias = null }
            : SqlJoinSyntax.BindProjection(p, alias, join)).ToList();
        var filter = cursor.Eat(SqlSyntax.Where) ? new SqlExpressionParser(cursor, operationLimitsOptions, execution.MaximumInValues, alias, join?.Alias).Parse() : null;
        var order = ReadOrder(projection, join);
        var (limit, offset) = SqlWindowSyntax.Read(cursor);
        cursor.Eat(SqlSyntax.Semicolon);
        if (cursor.Current.Kind != SqlTokenKind.End)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail);
        }
        if (limit < MinimumPositiveCount || offset < default(int) || (long)limit + offset > limits.MaxResults || projection.Count > execution.MaximumProjection || order.Count > execution.MaximumOrdering
            || projection.Select(p => p.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Count)
        {
            throw SqlSyntax.Invalid();
        }
        return new(collection, alias, [.. projection], filter, [.. order], limit, explain, modelSource, join, offset);
    }

    private SelectQuery ParseTable(bool explain)
    {
        var collection = cursor.Identifier();
        var order = ReadOrder([new(SqlSyntax.Star, SqlSyntax.Star)], null);
        var (limit, offset) = SqlWindowSyntax.Read(cursor);
        cursor.Eat(SqlSyntax.Semicolon);
        if (cursor.Current.Kind != SqlTokenKind.End)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail); }
        if (limit < MinimumPositiveCount || offset < default(int) || (long)limit + offset > limits.MaxResults
            || order.Count > execution.MaximumOrdering)
        { throw SqlSyntax.Invalid(); }
        return new(collection, null, [new(SqlSyntax.Star, SqlSyntax.Star)], null, [.. order], limit, explain,
            Offset: offset);
    }

    private List<Selection> ReadProjection()
    {
        var projection = new List<Selection>();
        if (cursor.Eat(SqlSyntax.Star))
        {
            projection.Add(new(SqlSyntax.Star, SqlSyntax.Star));
        }
        else
        {
            do
            {
                var path = cursor.Path();
                var output = SqlProjectionAliases.Read(cursor, path.Last());
                projection.Add(new(JsonData.Path(path.ToArray()), output));
            } while (cursor.Eat(SqlSyntax.Comma));
        }
        return projection;
    }

    private void ReadAlias()
    {
        if (cursor.Eat(SqlSyntax.As))
        {
            alias = cursor.Identifier();
        }
        else if (cursor.Current.Kind == SqlTokenKind.Identifier && !cursor.Is(SqlSyntax.Where) && !cursor.Is(SqlSyntax.Order)
            && !cursor.Is(SqlSyntax.Limit) && !cursor.Is(SqlSyntax.Offset) && !cursor.Is(SqlSyntax.Fetch)
            && !cursor.Is(SqlSyntax.Join)
            && (!cursor.Is(SqlSyntax.Inner) || !cursor.IsNext(SqlSyntax.Join)))
        {
            alias = cursor.Identifier();
        }
    }

    private bool? ReadNullPlacement()
    {
        if (!cursor.Eat(SqlSyntax.Nulls))
        { return null; }
        if (cursor.Eat(SqlSyntax.First))
        { return true; }
        cursor.Need(SqlSyntax.Last);
        return false;
    }

    private bool ReadDirection()
    {
        if (!cursor.Eat(SqlSyntax.Using))
        {
            var descending = cursor.Eat(SqlSyntax.Desc);
            if (!descending)
            { cursor.Eat(SqlSyntax.Asc); }
            return descending;
        }
        if (cursor.Eat(SqlSyntax.Less))
        { return false; }
        if (cursor.Eat(SqlSyntax.Greater))
        { return true; }
        if (cursor.Current.Quoted || cursor.Current.Kind is SqlTokenKind.End or SqlTokenKind.String
            or SqlTokenKind.Number or SqlTokenKind.Parameter || cursor.Is(SqlSyntax.Nulls)
            || cursor.Is(SqlSyntax.Limit) || cursor.Is(SqlSyntax.Offset) || cursor.Is(SqlSyntax.Fetch))
        { throw SqlSyntax.Invalid(); }
        throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail);
    }

    private List<Ordering> ReadOrder(List<Selection> projection, InnerJoinClause? join)
    {
        var order = new List<Ordering>();
        if (cursor.Eat(SqlSyntax.Order))
        {
            cursor.Need(SqlSyntax.By);
            do
            {
                var field = ReadOrderField(projection, join);
                var descending = ReadDirection();
                var nullsFirst = ReadNullPlacement();
                order.Add(new(field, descending, nullsFirst));
            } while (cursor.Eat(SqlSyntax.Comma));
        }
        return order;
    }

    private string ReadOrderField(List<Selection> projection, InnerJoinClause? join)
    {
        if (cursor.Current.Kind == SqlTokenKind.Number)
        { return SqlOrderOrdinal.Read(cursor, projection, join is not null, alias); }
        var raw = cursor.Path();
        return join is null ? SqlJoinSyntax.BoundPath(JsonData.Path(raw.ToArray()), alias)
            : SqlJoinSyntax.BoundOrderPath(raw, alias);
    }

}
