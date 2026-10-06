using KeyLoad.Query;
using Microsoft.Extensions.Options;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Server;

/// <summary>Scans the bounded CALL grammar without allocating tokens or evaluating application code.</summary>
internal sealed class SqlOperationSyntaxReader(string sql, int maximumTokens, int maximumDepth,
    IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken)
{
    private int offset;
    private int tokens;

    internal string Identifier()
    {
        SkipTrivia();
        if (offset == sql.Length || !IdentifierStart(sql[offset]))
        { throw SqlOperationSyntax.InvalidInput(); }
        CountToken();
        var start = offset++;
        while (offset < sql.Length && IdentifierPart(sql[offset]))
        { Advance(); }
        return sql[start..offset];
    }

    internal void Need(char symbol)
    {
        SkipTrivia();
        if (offset == sql.Length || sql[offset] != symbol)
        { throw SqlOperationSyntax.InvalidInput(); }
        CountToken();
        Advance();
    }

    internal void Complete()
    {
        SkipTrivia();
        if (offset < sql.Length && sql[offset] == SqlOperationSyntax.Terminator)
        {
            CountToken();
            Advance();
            SkipTrivia();
        }
        if (offset != sql.Length)
        { throw SqlOperationSyntax.InvalidInput(); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal void SkipTrivia()
    {
        var state = default(SqlTriviaState);
        SqlTriviaStatus status;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = SqlTriviaReader.Read(sql.AsSpan(), ref offset, ref state, maximumDepth);
        }
        while (status == SqlTriviaStatus.More);
        cancellationToken.ThrowIfCancellationRequested();
        if (status == SqlTriviaStatus.UnterminatedComment)
        { throw SqlOperationSyntax.InvalidInput(); }
        if (status == SqlTriviaStatus.DepthLimitExceeded)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.StructureExceeded); }
    }

    private void Advance()
    {
        const int EmptyOffsetSqlOperationSyntaxCheckInterval = 0;

        offset++;
        if (offset % queryOptions.Value.SqlBudgetCheckInterval == EmptyOffsetSqlOperationSyntaxCheckInterval)
        { cancellationToken.ThrowIfCancellationRequested(); }
    }

    private void CountToken()
    {
        if (++tokens > maximumTokens)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.StructureExceeded); }
    }

    private static bool IdentifierStart(char value) => value == SqlOperationSyntax.Underscore
        || value is >= SqlOperationSyntax.FirstUpper and <= SqlOperationSyntax.LastUpper
        or >= SqlOperationSyntax.FirstLower and <= SqlOperationSyntax.LastLower;

    private static bool IdentifierPart(char value) => IdentifierStart(value)
        || value is >= SqlOperationSyntax.FirstDigit and <= SqlOperationSyntax.LastDigit;
}
