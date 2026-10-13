using System.Text;
using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Token categories recognized by the bounded Q1 SQL dialect.</summary>
internal enum SqlTokenKind { Identifier, String, Number, Parameter, Symbol, End }

/// <summary>One lexical Q1 token, retaining quoted-identifier provenance.</summary>
internal sealed record SqlToken(SqlTokenKind Kind, string Text, bool Quoted = false);

/// <summary>Lexes Q1 SQL without evaluating application expressions.</summary>
internal static class SqlTokenizer
{
    private const int FirstElementIndex = 0;
    private const char SqlStringQuote = '\'';
    private const char SqlIdentifierQuote = '"';
    private const char IdentifierSeparator = '_';
    private const char ParameterPrefix = '@';
    private const char MinusCharacter = '-';
    private const int AdjacentElementOffset = 1;
    private const char DecimalPoint = '.';
    private const char LowerExponentMarker = 'e';
    private const char UpperExponentMarker = 'E';
    private const char PlusCharacter = '+';
    private const char GreaterCharacter = '>';
    private const char LessCharacter = '<';
    private const char NotCharacter = '!';
    private const char EqualCharacter = '=';
    private const int EmptyElementCount = 0;
    private const int BudgetCheckRemainder = 0;

    internal static List<SqlToken> Lex(string sql, int maximum, int maximumDepth, int budgetCheckInterval,
        ReadExecutionBudget? budget = null)
    {
        var result = new List<SqlToken>();
        var sourceTokenCount = EmptyElementCount;
        var triviaState = default(SqlTriviaState);
        for (var index = FirstElementIndex; index < sql.Length;)
        {
            budget?.Check();
            var status = ReadTrivia(sql, ref index, ref triviaState, maximumDepth, budgetCheckInterval, budget);
            if (status == SqlTriviaStatus.UnterminatedComment)
            {
                throw Errors.Fail(ErrorCode.Validation, SqlCommentSyntax.UnterminatedBlockDetail);
            }
            if (status == SqlTriviaStatus.DepthLimitExceeded)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, SqlCommentSyntax.BlockDepthDetail);
            }
            if (index >= sql.Length)
            {
                break;
            }
            var current = sql[index];
            ChargeContinuation(ref sourceTokenCount, maximum);
            result.Add(current switch
            {
                _ when SqlUnicodeStringReader.Starts(sql, index) => SqlUnicodeStringReader.Read(sql, ref index, budget, budgetCheckInterval, ref sourceTokenCount, maximum, maximumDepth),
                _ when SqlEscapeStringReader.Starts(sql, index) => SqlEscapeStringReader.Read(sql, ref index, budget, budgetCheckInterval, ref sourceTokenCount, maximum),
                SqlStringQuote or SqlIdentifierQuote => Quoted(sql, ref index, budget, budgetCheckInterval, ref sourceTokenCount, maximum),
                global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Delimiter => SqlDollarStringReader.Read(sql, ref index, budget, budgetCheckInterval),
                _ when char.IsLetter(current) || current is IdentifierSeparator or ParameterPrefix => Identifier(sql, ref index, budget, budgetCheckInterval),
                _ when SqlNumericLiteralStart.Read(sql, index)
                    => Number(sql, ref index, budget, budgetCheckInterval),
                _ => Symbol(sql, ref index)
            });
        }
        result.Add(new(SqlTokenKind.End, string.Empty));
        return result;
    }

    internal static void ChargeContinuation(ref int count, int maximum)
    {
        if (count >= maximum)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.TokenBudgetDetail); }
        count++;
    }

    private static SqlTriviaStatus ReadTrivia(string sql, ref int index, ref SqlTriviaState state,
        int maximumDepth, int budgetCheckInterval, ReadExecutionBudget? budget)
    {
        SqlTriviaStatus status;
        do
        {
            status = SqlTriviaReader.Read(sql.AsSpan(), ref index, ref state, maximumDepth, budgetCheckInterval);
            if (status == SqlTriviaStatus.More)
            {
                budget?.Check();
            }
        } while (status == SqlTriviaStatus.More);
        return status;
    }

    private static SqlToken Quoted(string sql, ref int index, ReadExecutionBudget? budget, int budgetCheckInterval,
        ref int sourceTokenCount, int maximum)
    {
        var quote = sql[index++];
        var value = new StringBuilder();
        var closed = false;
        while (index < sql.Length)
        {
            CheckBudget(budget, index, budgetCheckInterval);
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
                if (quote == SqlStringQuote && SqlStringContinuation.Consume(sql, ref index, budget, budgetCheckInterval))
                { ChargeContinuation(ref sourceTokenCount, maximum); index++; continue; }
                closed = true;
                break;
            }
        }
        if (!closed)
        {
            throw SqlSyntax.Invalid();
        }
        return new(quote == SqlStringQuote ? SqlTokenKind.String : SqlTokenKind.Identifier, value.ToString(), quote == SqlIdentifierQuote);
    }

    private static SqlToken Identifier(string sql, ref int index, ReadExecutionBudget? budget, int budgetCheckInterval)
    {
        var start = index++;
        while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == IdentifierSeparator))
        {
            index++;
            CheckBudget(budget, index, budgetCheckInterval);
        }
        var parameter = sql[start] == ParameterPrefix;
        return new(parameter ? SqlTokenKind.Parameter : SqlTokenKind.Identifier,
            sql[(parameter ? start + AdjacentElementOffset : start)..index]);
    }

    private static SqlToken Number(string sql, ref int index, ReadExecutionBudget? budget, int budgetCheckInterval)
    {
        var start = index++;
        if (SqlRadixIntegerReader.Read(sql, start, ref index, budget, budgetCheckInterval))
        { return new(SqlTokenKind.Number, sql[start..index]); }
        while (index < sql.Length && !StartsLineComment(sql, index)
            && (char.IsDigit(sql[index]) || sql[index] is DecimalPoint or LowerExponentMarker or UpperExponentMarker or PlusCharacter or MinusCharacter))
        {
            index++;
            CheckBudget(budget, index, budgetCheckInterval);
        }
        return new(SqlTokenKind.Number, sql[start..index]);
    }

    private static bool StartsLineComment(string sql, int index)
        => sql[index] == SqlTriviaSyntax.Dash && index + AdjacentElementOffset < sql.Length && sql[index + AdjacentElementOffset] == SqlTriviaSyntax.Dash;

    private static SqlToken Symbol(string sql, ref int index)
    {
        var current = sql[index++];
        var text = current.ToString();
        if (index < sql.Length && (current is GreaterCharacter or LessCharacter or NotCharacter && sql[index] == EqualCharacter || current == LessCharacter && sql[index] == GreaterCharacter))
        {
            text += sql[index++];
        }
        if (text is not (SqlSyntax.OpenParen or SqlSyntax.CloseParen or SqlSyntax.Comma or SqlSyntax.Dot or SqlSyntax.Star or SqlSyntax.Semicolon or SqlSyntax.Equals or SqlSyntax.NotEquals or SqlSyntax.AlternateNotEquals or SqlSyntax.Greater or SqlSyntax.GreaterOrEqual or SqlSyntax.Less or SqlSyntax.LessOrEqual))
        {
            throw SqlSyntax.Invalid();
        }
        return new(SqlTokenKind.Symbol, text);
    }

    private static void CheckBudget(ReadExecutionBudget? budget, int index, int budgetCheckInterval)
    {
        if (index > EmptyElementCount && index % budgetCheckInterval == BudgetCheckRemainder)
        {
            budget?.Check();
        }
    }
}
