using System.Buffers;
using System.Text;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlEscapeStringReader
{
    private const char Quote = '\'';
    private const char Slash = '\\';
    private const char LowerPrefix = 'e';
    private const char UpperPrefix = 'E';
    private const char Nul = '\0';
    private const int PrefixWidth = 2;
    private const int Adjacent = 1;
    private const int Zero = 0;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool Starts(string sql, int index)
        => sql[index] is LowerPrefix or UpperPrefix && index + Adjacent < sql.Length
            && sql[index + Adjacent] == Quote;

    internal static SqlToken Read(string sql, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget, int interval,
        ref int sourceTokenCount, int maximum)
    {
        index += PrefixWidth;
        var output = new ArrayBufferWriter<byte>();
        while (index < sql.Length)
        {
            budget?.Check();
            var current = sql[index++];
            if (current == Quote)
            {
                if (index < sql.Length && sql[index] == Quote)
                { index++; SqlEscapeStringCodec.Write(new Rune(Quote), output); continue; }
                if (SqlStringContinuation.Consume(sql, ref index, budget, interval))
                { SqlTokenizer.ChargeContinuation(ref sourceTokenCount, maximum); index++; continue; }
                return Finish(output);
            }
            if (current == Slash)
            { SqlEscapeStringCodec.Escape(sql, ref index, output, budget, interval); }
            else
            { SqlEscapeStringCodec.Literal(sql, ref index, output); }
        }
        throw SqlSyntax.Invalid();
    }

    private static SqlToken Finish(ArrayBufferWriter<byte> output)
    {
        string value;
        try
        { value = Utf8.GetString(output.WrittenSpan); }
        catch (DecoderFallbackException)
        { throw SqlSyntax.Invalid(); }
        if (value.Contains(Nul, global::System.StringComparison.Ordinal))
        { throw SqlSyntax.Invalid(); }
        return new(SqlTokenKind.String, value);
    }

    internal static void Check(global::KeyLoad.Core.ReadExecutionBudget? budget, int index, int interval)
    {
        if (index > Zero && index % interval == Zero)
        { budget?.Check(); }
    }
}
