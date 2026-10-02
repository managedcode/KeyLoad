namespace KeyLoad.Server;

/// <summary>Scans the bounded CALL grammar without allocating tokens or evaluating application code.</summary>
internal sealed class SqlOperationSyntaxReader(string sql, int maximumTokens, CancellationToken cancellationToken)
{
    private int offset;
    private int tokens;

    internal string Identifier()
    {
        Whitespace();
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
        Whitespace();
        if (offset == sql.Length || sql[offset] != symbol)
        { throw SqlOperationSyntax.InvalidInput(); }
        CountToken();
        Advance();
    }

    internal void Complete()
    {
        Whitespace();
        if (offset < sql.Length && sql[offset] == SqlOperationSyntax.Terminator)
        {
            CountToken();
            Advance();
            Whitespace();
        }
        if (offset != sql.Length)
        { throw SqlOperationSyntax.InvalidInput(); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Whitespace()
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (offset < sql.Length && char.IsWhiteSpace(sql[offset]))
        { Advance(); }
    }

    private void Advance()
    {
        offset++;
        if (offset % SqlOperationSyntax.CheckInterval == 0)
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
