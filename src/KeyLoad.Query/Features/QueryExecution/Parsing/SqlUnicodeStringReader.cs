using System.Text;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlUnicodeStringReader
{
    private const char LowerPrefix = 'u';
    private const char UpperPrefix = 'U';
    private const char Marker = '&';
    private const char Quote = '\'';
    private const int PrefixWidth = 3;
    private const int Next = 1;
    private const int MarkerOffset = 1;
    private const int QuoteOffset = 2;

    internal static bool Starts(string sql, int index)
        => sql[index] is LowerPrefix or UpperPrefix && index + QuoteOffset < sql.Length
            && sql[index + MarkerOffset] == Marker && sql[index + QuoteOffset] == Quote;

    internal static SqlToken Read(string sql, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval, ref int sourceTokenCount, int maximum, int maximumDepth)
    {
        index += PrefixWidth - Next;
        var raw = Quoted(sql, ref index, budget, interval, ref sourceTokenCount, maximum);
        var escape = SqlUnicodeEscapeClause.Read(sql, ref index, budget, interval, ref sourceTokenCount,
            maximum, maximumDepth);
        return new(SqlTokenKind.String, SqlUnicodeStringCodec.Decode(raw, escape, budget, interval));
    }

    internal static string Quoted(string sql, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval, ref int sourceTokenCount, int maximum)
    {
        if (index >= sql.Length || sql[index++] != Quote)
        { throw SqlSyntax.Invalid(); }
        var raw = new StringBuilder();
        while (index < sql.Length)
        {
            budget?.Check();
            var current = sql[index++];
            if (current != Quote)
            { raw.Append(current); continue; }
            if (index < sql.Length && sql[index] == Quote)
            { raw.Append(Quote); index++; continue; }
            if (SqlStringContinuation.Consume(sql, ref index, budget, interval))
            { SqlTokenizer.ChargeContinuation(ref sourceTokenCount, maximum); index++; continue; }
            return raw.ToString();
        }
        throw SqlSyntax.Invalid();
    }
}
