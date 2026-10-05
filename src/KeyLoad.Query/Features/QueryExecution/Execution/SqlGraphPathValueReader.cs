using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Reads exact string and Int32 literals or matching scalar parameters.</summary>
internal sealed class SqlGraphPathValueReader(SqlTokenCursor cursor, QueryRequest query, ReadExecutionBudget budget)
{
    internal string ReadString()
    {
        budget.Check();
        var token = cursor.Current;
        if (token.Kind == SqlTokenKind.String)
        {
            cursor.Advance();
            return token.Text;
        }
        if (token.Kind == SqlTokenKind.Parameter && query.Parameters is { } parameters
            && parameters.TryGetValue(token.Text, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text)
        {
            cursor.Advance();
            return text;
        }
        throw token.Kind == SqlTokenKind.Parameter
            ? Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.ParameterDetail)
            : SqlGraphPathSyntax.Invalid();
    }

    internal int ReadInteger()
    {
        budget.Check();
        var token = cursor.Current;
        if (token.Kind == SqlTokenKind.Number
            && int.TryParse(token.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var literal))
        {
            cursor.Advance();
            return literal;
        }
        if (token.Kind == SqlTokenKind.Parameter && query.Parameters is { } parameters
            && parameters.TryGetValue(token.Text, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parameter))
        {
            cursor.Advance();
            return parameter;
        }
        throw token.Kind == SqlTokenKind.Parameter
            ? Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.ParameterDetail)
            : SqlGraphPathSyntax.Invalid();
    }

    internal ImmutableArray<string>? ReadOptionalLabels()
    {
        if (!cursor.Eat(SqlSyntax.Comma))
        {
            return null;
        }
        var labels = ImmutableArray.CreateBuilder<string>();
        do
        {
            budget.Check();
            if (labels.Count == SqlGraphPathSyntax.MaximumLabels)
            {
                throw SqlGraphPathSyntax.Invalid();
            }
            labels.Add(ReadString());
        } while (cursor.Eat(SqlSyntax.Comma));
        return labels.ToImmutable();
    }
}
