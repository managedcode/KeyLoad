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
    private static readonly DatabaseLimits Bounds = new();

    internal static bool MayWrite(string? sql, CancellationToken cancellationToken)
    {
        if (sql is null || sql.Length > Bounds.MaxQueryBytes
            || Encoding.UTF8.GetByteCount(sql) > Bounds.MaxQueryBytes)
        { return true; }
        var offset = 0;
        if (!SkipTrivia(sql, ref offset, cancellationToken))
        { return true; }
        if (Keyword(sql, ref offset, Select, cancellationToken))
        { return false; }
        return !Keyword(sql, ref offset, Explain, cancellationToken) || !SkipTrivia(sql, ref offset, cancellationToken)
            || !Keyword(sql, ref offset, Select, cancellationToken);
    }

    private static bool SkipTrivia(ReadOnlySpan<char> sql, ref int offset, CancellationToken cancellationToken)
    {
        var state = new SqlTriviaState();
        var end = cancellationToken.IsCancellationRequested
            ? Math.Min(sql.Length, SqlTriviaReader.MaximumChunkCharacters) : sql.Length;
        if (offset > end)
        { return false; }
        SqlTriviaStatus status;
        do
        {
            status = SqlTriviaReader.Read(sql[..end], ref offset, ref state, Bounds.MaxQueryDepth);
            if (status == SqlTriviaStatus.More && cancellationToken.IsCancellationRequested)
            { return false; }
        } while (status == SqlTriviaStatus.More);
        return status == SqlTriviaStatus.Complete && (offset < end || end == sql.Length);
    }

    private static bool Keyword(ReadOnlySpan<char> sql, ref int offset, ReadOnlySpan<char> keyword,
        CancellationToken cancellationToken)
    {
        var boundary = offset + keyword.Length;
        if (cancellationToken.IsCancellationRequested
            && (boundary > SqlTriviaReader.MaximumChunkCharacters
                || boundary == SqlTriviaReader.MaximumChunkCharacters && boundary < sql.Length))
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
