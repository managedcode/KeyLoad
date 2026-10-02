using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Token categories recognized by the bounded Q1 SQL dialect.</summary>
internal enum SqlTokenKind { Identifier, String, Number, Parameter, Symbol, End }

/// <summary>One lexical Q1 token, retaining quoted-identifier provenance.</summary>
internal sealed record SqlToken(SqlTokenKind Kind, string Text, bool Quoted = false);

/// <summary>Lexes Q1 SQL without evaluating application expressions.</summary>
internal static class SqlTokenizer
{
    private const int BudgetCheckInterval = 256;

    internal static List<SqlToken> Lex(string sql, int maximum, ReadExecutionBudget? budget = null)
    {
        var result = new List<SqlToken>();
        for (var index = 0; index < sql.Length;)
        {
            CheckBudget(budget, index);
            var current = sql[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (result.Count >= maximum)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.TokenBudgetDetail);
            }
            result.Add(current switch
            {
                '\'' or '"' => Quoted(sql, ref index, budget),
                _ when char.IsLetter(current) || current is '_' or '@' => Identifier(sql, ref index, budget),
                _ when char.IsDigit(current) || current == '-' && index + 1 < sql.Length && char.IsDigit(sql[index + 1])
                    => Number(sql, ref index, budget),
                _ => Symbol(sql, ref index)
            });
        }
        result.Add(new(SqlTokenKind.End, string.Empty));
        return result;
    }

    private static SqlToken Quoted(string sql, ref int index, ReadExecutionBudget? budget)
    {
        var quote = sql[index++];
        var value = new StringBuilder();
        var closed = false;
        while (index < sql.Length)
        {
            CheckBudget(budget, index);
            if (sql[index] != quote)
            {
                value.Append(sql[index++]);
                continue;
            }
            index++;
            if (index < sql.Length && sql[index] == quote)
            {
                value.Append(quote);
                index++;
            }
            else
            {
                closed = true;
                break;
            }
        }
        if (!closed)
        {
            throw SqlSyntax.Invalid();
        }
        return new(quote == '\'' ? SqlTokenKind.String : SqlTokenKind.Identifier, value.ToString(), quote == '"');
    }

    private static SqlToken Identifier(string sql, ref int index, ReadExecutionBudget? budget)
    {
        var start = index++;
        while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_'))
        {
            index++;
            CheckBudget(budget, index);
        }
        var parameter = sql[start] == '@';
        return new(parameter ? SqlTokenKind.Parameter : SqlTokenKind.Identifier,
            sql[(parameter ? start + 1 : start)..index]);
    }

    private static SqlToken Number(string sql, ref int index, ReadExecutionBudget? budget)
    {
        var start = index++;
        while (index < sql.Length && (char.IsDigit(sql[index]) || sql[index] is '.' or 'e' or 'E' or '+' or '-'))
        {
            index++;
            CheckBudget(budget, index);
        }
        return new(SqlTokenKind.Number, sql[start..index]);
    }

    private static SqlToken Symbol(string sql, ref int index)
    {
        var current = sql[index++];
        var text = current.ToString();
        if (index < sql.Length && (current is '>' or '<' or '!' && sql[index] == '=' || current == '<' && sql[index] == '>'))
        {
            text += sql[index++];
        }
        if (text is not (SqlSyntax.OpenParen or SqlSyntax.CloseParen or SqlSyntax.Comma or SqlSyntax.Dot or SqlSyntax.Star or SqlSyntax.Semicolon or SqlSyntax.Equals or SqlSyntax.NotEquals or SqlSyntax.AlternateNotEquals or SqlSyntax.Greater or SqlSyntax.GreaterOrEqual or SqlSyntax.Less or SqlSyntax.LessOrEqual))
        {
            throw SqlSyntax.Invalid();
        }
        return new(SqlTokenKind.Symbol, text);
    }

    private static void CheckBudget(ReadExecutionBudget? budget, int index)
    {
        if (index > 0 && index % BudgetCheckInterval == 0)
        {
            budget?.Check();
        }
    }
}
