using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Query;

/// <summary>Reads bounded scalar, vector, seed and list values from a graph-search token cursor.</summary>
internal sealed class SqlGraphSearchValueReader
{
    private readonly SqlTokenCursor cursor;
    private readonly QueryRequest query;
    private readonly ReadExecutionBudget budget;

    internal SqlGraphSearchValueReader(SqlTokenCursor cursor, QueryRequest query, ReadExecutionBudget budget)
    {
        this.cursor = cursor;
        this.query = query;
        this.budget = budget;
    }

    internal ImmutableArray<EntityRef> ReadSeeds()
    {
        var seeds = ImmutableArray.CreateBuilder<EntityRef>();
        cursor.Need(SqlSyntax.OpenParen);
        cursor.Need(SqlSyntax.OpenParen);
        do
        {
            budget.Check();
            var collection = cursor.Identifier();
            cursor.Need(SqlSyntax.Comma);
            seeds.Add(new(query.Partition, collection, ReadStringValue()));
            cursor.Need(SqlSyntax.CloseParen);
        } while (cursor.Eat(SqlSyntax.Comma));
        cursor.Need(SqlSyntax.CloseParen);
        return seeds.Count == 0 ? throw SqlGraphSearchSyntax.Invalid() : seeds.ToImmutable();
    }

    internal ImmutableArray<string>? ReadOptionalLabels()
        => cursor.Eat(SqlGraphSearchSyntax.Labels) ? ReadStringList() : null;

    internal ImmutableArray<string>? ReadAllowedIds()
        => cursor.Eat(SqlGraphSearchSyntax.Allow) ? ReadIdsList() : null;

    internal string ReadStringValue()
    {
        budget.Check();
        var token = cursor.Current;
        if (token.Kind == SqlTokenKind.String)
        {
            cursor.Advance();
            return token.Text;
        }
        if (token.Kind == SqlTokenKind.Parameter)
        {
            cursor.Advance();
            var parameters = query.Parameters;
            if (parameters is null || !parameters.TryGetValue(token.Text, out var value)
                || value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
            {
                throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.ParameterDetail);
            }
            return text;
        }
        throw SqlGraphSearchSyntax.Invalid();
    }

    internal ImmutableArray<float> ReadVectorParameter()
    {
        var token = cursor.Current;
        var parameters = query.Parameters;
        if (token.Kind != SqlTokenKind.Parameter || parameters is null
            || !parameters.TryGetValue(token.Text, out var value) || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() is < 1 or > SqlGraphSearchSyntax.MaximumVectorDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.ParameterDetail);
        }
        cursor.Advance();
        var components = ImmutableArray.CreateBuilder<float>(value.GetArrayLength());
        foreach (var component in value.EnumerateArray())
        {
            budget.Check();
            if (component.ValueKind != JsonValueKind.Number || !component.TryGetSingle(out var number) || !float.IsFinite(number))
            {
                throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
            }
            components.Add(number);
        }
        return components.ToImmutable();
    }

    internal VectorSpace ReadSpace()
    {
        cursor.Need(SqlSyntax.OpenParen);
        var id = cursor.Identifier();
        cursor.Need(SqlSyntax.Comma);
        var dimension = ReadInteger();
        cursor.Need(SqlSyntax.Comma);
        var metricName = cursor.Identifier();
        cursor.Need(SqlSyntax.Comma);
        var model = cursor.Identifier();
        cursor.Need(SqlSyntax.Comma);
        var version = cursor.Identifier();
        cursor.Need(SqlSyntax.CloseParen);
        if (!Enum.TryParse<DistanceMetric>(metricName, ignoreCase: true, out var metric)
            || !Enum.IsDefined(metric) || dimension is < 1 or > SqlGraphSearchSyntax.MaximumVectorDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
        }
        return new(id, dimension, metric, model, version);
    }

    internal string ReadField()
    {
        budget.Check();
        var field = JsonData.Path(cursor.Path().ToArray());
        budget.Check();
        return field;
    }

    internal int ReadOptionalInt(string keyword, int fallback)
        => cursor.Eat(keyword) ? ReadInteger() : fallback;

    internal int ReadInteger()
    {
        if (cursor.Current.Kind != SqlTokenKind.Number
            || !int.TryParse(cursor.Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw SqlGraphSearchSyntax.Invalid();
        }
        cursor.Advance();
        return value;
    }

    internal double ReadOptionalWeight()
    {
        if (!cursor.Eat(SqlGraphSearchSyntax.Weight))
        {
            return SqlGraphSearchSyntax.DefaultWeight;
        }
        if (cursor.Current.Kind != SqlTokenKind.Number
            || !double.TryParse(cursor.Current.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || value < 0)
        {
            throw SqlGraphSearchSyntax.Invalid();
        }
        cursor.Advance();
        return value;
    }

    private ImmutableArray<string> ReadIdsList()
    {
        cursor.Need(SqlGraphSearchSyntax.Ids);
        return ReadStringList();
    }

    private ImmutableArray<string> ReadStringList()
    {
        var values = ImmutableArray.CreateBuilder<string>();
        cursor.Need(SqlSyntax.OpenParen);
        if (cursor.Eat(SqlSyntax.CloseParen))
        {
            return values.ToImmutable();
        }
        do
        {
            budget.Check();
            values.Add(ReadStringValue());
        } while (cursor.Eat(SqlSyntax.Comma));
        cursor.Need(SqlSyntax.CloseParen);
        return values.ToImmutable();
    }
}
