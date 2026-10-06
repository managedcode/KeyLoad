using System.Globalization;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>A bounded Q1 parser. Unsupported syntax is rejected, never passed through to storage.</summary>
public sealed class SqlParser
{
    private const int MinimumPositiveCount = 1;
    private const long FirstOrdinalLong = 1L;
    private const int FirstElementIndex = 0;
    private const int SinglePathSegment = 1;

    private readonly SqlTokenCursor cursor;
    private readonly DatabaseLimits limits;
    private readonly QueryExecutionOptions execution;
    private readonly IOptions<DatabaseLimits> operationLimitsOptions;
    private string? alias;

    /// <summary>Creates a parser with the configured SQL byte and token limits.</summary>
    /// <param name="sql">Q1 SQL text.</param>
    /// <param name="limitsOptions">Centrally validated database query limits.</param>
    /// <param name="executionOptions">Centrally validated query structure limits.</param>
    public SqlParser(string sql, IOptions<DatabaseLimits> limitsOptions, IOptions<QueryExecutionOptions> executionOptions)
        : this(sql, limitsOptions, executionOptions, null)
    {
    }

    internal SqlParser(string sql, IOptions<DatabaseLimits> limitsOptions, IOptions<QueryExecutionOptions> executionOptions,
        ReadExecutionBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        ArgumentNullException.ThrowIfNull(executionOptions);
        limits = limitsOptions.Value;
        limits.Validate();
        execution = executionOptions.Value;
        execution.Validate();
        operationLimitsOptions = limitsOptions;
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
        cursor.Need(SqlSyntax.Select);
        var projection = ReadProjection();
        cursor.Need(SqlSyntax.From);
        var (collection, modelSource) = ReadSource();
        ReadAlias();
        projection = projection.Select(p => p with { Path = BoundPath(p.Path) }).ToList();
        var filter = cursor.Eat(SqlSyntax.Where) ? new SqlExpressionParser(cursor, operationLimitsOptions, execution.MaximumInValues, alias).Parse() : null;
        var order = ReadOrder();
        var limit = ReadLimit();
        cursor.Eat(SqlSyntax.Semicolon);
        if (cursor.Current.Kind != SqlTokenKind.End)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedSyntaxDetail);
        }
        if (limit < MinimumPositiveCount || limit > limits.MaxResults || projection.Count > execution.MaximumProjection || order.Count > execution.MaximumOrdering
            || projection.Select(p => p.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Count)
        {
            throw SqlSyntax.Invalid();
        }
        return new(collection, alias, [.. projection], filter, [.. order], limit, explain, modelSource);
    }

    private (string Collection, ModelQuerySource? Source) ReadSource()
    {
        if (!cursor.Current.Quoted && cursor.Is(SqlSyntax.Events))
        {
            var collection = cursor.Current.Text;
            cursor.Advance();
            if (!cursor.Eat(SqlSyntax.OpenParen))
            {
                return (collection, null);
            }
            var streamSet = StringArgument();
            cursor.Need(SqlSyntax.Comma);
            var streamId = StringArgument();
            var generation = FirstOrdinalLong;
            if (cursor.Eat(SqlSyntax.Comma))
            {
                if (cursor.Current.Kind != SqlTokenKind.Number
                    || !long.TryParse(cursor.Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out generation)
                    || generation < MinimumPositiveCount)
                {
                    throw SqlSyntax.Invalid();
                }
                cursor.Advance();
            }
            cursor.Need(SqlSyntax.CloseParen);
            return (streamSet, new(ModelQuerySourceKind.Events, streamId, generation));
        }

        if (!cursor.Current.Quoted && cursor.Is(SqlSyntax.QueueMessages))
        {
            var collection = cursor.Current.Text;
            cursor.Advance();
            if (!cursor.Eat(SqlSyntax.OpenParen))
            {
                return (collection, null);
            }
            var queue = StringArgument();
            cursor.Need(SqlSyntax.CloseParen);
            return (queue, new(ModelQuerySourceKind.QueueMessages, queue));
        }

        return (cursor.Identifier(), null);
    }

    private string StringArgument()
    {
        if (cursor.Current.Kind != SqlTokenKind.String)
        {
            throw SqlSyntax.Invalid();
        }
        var value = cursor.Current.Text;
        cursor.Advance();
        JsonData.Identifier(value);
        return value;
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
                var output = cursor.Eat(SqlSyntax.As) ? cursor.Identifier() : path.Last();
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
        else if (cursor.Current.Kind == SqlTokenKind.Identifier && !cursor.Is(SqlSyntax.Where) && !cursor.Is(SqlSyntax.Order) && !cursor.Is(SqlSyntax.Limit))
        {
            alias = cursor.Identifier();
        }
    }

    private string BoundPath(string path)
    {
        if (path == SqlSyntax.Star)
        {
            return path;
        }
        var parts = JsonData.PathSegments(path).ToList();
        if (alias is not null && parts.Count > MinimumPositiveCount && parts[FirstElementIndex] == alias)
        {
            parts.RemoveAt(FirstElementIndex);
        }
        return parts.Count == SinglePathSegment && parts[FirstElementIndex] is SqlSyntax.MetadataId or SqlSyntax.MetadataRevision ? SqlSyntax.MetadataPrefix + parts[FirstElementIndex] : JsonData.Path(parts.ToArray());
    }

    private List<Ordering> ReadOrder()
    {
        var order = new List<Ordering>();
        if (cursor.Eat(SqlSyntax.Order))
        {
            cursor.Need(SqlSyntax.By);
            do
            {
                var field = BoundPath(JsonData.Path(cursor.Path().ToArray()));
                var descending = cursor.Eat(SqlSyntax.Desc);
                if (!descending)
                {
                    cursor.Eat(SqlSyntax.Asc);
                }
                order.Add(new(field, descending));
            } while (cursor.Eat(SqlSyntax.Comma));
        }
        return order;
    }

    private int ReadLimit()
    {
        var limit = SqlSyntax.DefaultLimit;
        if (cursor.Eat(SqlSyntax.Limit))
        {
            if (cursor.Current.Kind != SqlTokenKind.Number
                || !int.TryParse(cursor.Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out limit))
            {
                throw SqlSyntax.Invalid();
            }
            cursor.Advance();
        }
        return limit;
    }
}
