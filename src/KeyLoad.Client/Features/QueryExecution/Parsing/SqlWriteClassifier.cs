using System.Text;
using KeyLoad.Features.QueryExecution;

namespace KeyLoad.Client;

/// <summary>Bounds local prefix inspection and preserves unknown write outcomes.</summary>
internal static class SqlWriteClassifier
{
    private const string Select = "SELECT";
    private const string Explain = "EXPLAIN";
    private const char Underscore = '_';
    private const char Dollar = '$';
    private const char Parameter = '@';
    private const int SqlStartOffset = 0;

    internal static bool MayWrite(string? sql, KeyLoadClientExecutionOptions bounds, CancellationToken cancellationToken)
    {
        if (sql is null || sql.Length > bounds.MaximumSqlInspectionBytes
            || Encoding.UTF8.GetByteCount(sql) > bounds.MaximumSqlInspectionBytes)
        { return true; }
        var budgetCheckInterval = bounds.SqlBudgetCheckInterval;
        var offset = SqlStartOffset;
        if (!SkipTrivia(sql, ref offset, bounds.MaximumSqlInspectionDepth, budgetCheckInterval, cancellationToken))
        { return true; }
        if (Keyword(sql, ref offset, Select, budgetCheckInterval, cancellationToken))
        { return false; }
        return !Keyword(sql, ref offset, Explain, budgetCheckInterval, cancellationToken)
            || !SkipTrivia(sql, ref offset, bounds.MaximumSqlInspectionDepth, budgetCheckInterval, cancellationToken)
            || !Keyword(sql, ref offset, Select, budgetCheckInterval, cancellationToken);
    }

    private static bool SkipTrivia(ReadOnlySpan<char> sql, ref int offset, int maximumDepth,
        int budgetCheckInterval, CancellationToken cancellationToken)
    {
        var state = new SqlTriviaState();
        var end = cancellationToken.IsCancellationRequested
            ? Math.Min(sql.Length, budgetCheckInterval) : sql.Length;
        if (offset > end)
        { return false; }
        SqlTriviaStatus status;
        do
        {
            status = SqlTriviaReader.Read(sql[..end], ref offset, ref state, maximumDepth, budgetCheckInterval);
            if (status == SqlTriviaStatus.More && cancellationToken.IsCancellationRequested)
            { return false; }
        } while (status == SqlTriviaStatus.More);
        return status == SqlTriviaStatus.Complete && (offset < end || end == sql.Length);
    }

    private static bool Keyword(ReadOnlySpan<char> sql, ref int offset, ReadOnlySpan<char> keyword,
        int budgetCheckInterval, CancellationToken cancellationToken)
    {
        var boundary = offset + keyword.Length;
        if (cancellationToken.IsCancellationRequested
            && (boundary > budgetCheckInterval
                || boundary == budgetCheckInterval && boundary < sql.Length))
        { return false; }
        var remaining = sql[offset..];
        if (!remaining.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            || remaining.Length > keyword.Length && IdentifierPart(remaining[keyword.Length]))
        { return false; }
        offset += keyword.Length;
        return true;
    }

    private static bool IdentifierPart(char value) => char.IsLetterOrDigit(value)
        || value is Underscore or Dollar or Parameter;
}
