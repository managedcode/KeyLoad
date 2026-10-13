using System.Buffers;
using System.Text;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlUnicodeStringCodec
{
    private const char Plus = '+';
    private const char Nul = '\0';
    private const int First = 0;
    private const int Next = 1;
    private const int NarrowDigits = 4;
    private const int WideDigits = 6;
    private const uint HighFirst = 0xd800;
    private const uint HighLast = 0xdbff;

    internal static string Decode(string raw, char escape, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval)
    {
        var output = new ArrayBufferWriter<byte>();
        var index = First;
        while (index < raw.Length)
        {
            budget?.Check();
            var current = raw[index++];
            if (current != escape)
            { SqlEscapeStringCodec.Literal(raw, ref index, output); continue; }
            if (index < raw.Length && raw[index] == escape)
            { index++; SqlEscapeStringCodec.Write(new Rune(escape), output); continue; }
            SqlEscapeStringCodec.Write(Unicode(raw, ref index, escape, budget, interval), output);
        }
        var value = Encoding.UTF8.GetString(output.WrittenSpan);
        if (value.Contains(Nul, global::System.StringComparison.Ordinal))
        { throw SqlSyntax.Invalid(); }
        return value;
    }

    private static Rune Unicode(string raw, ref int index, char escape,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int interval)
    {
        var value = Point(raw, ref index, budget, interval);
        if (value is >= HighFirst and <= HighLast)
        {
            if (index >= raw.Length || raw[index++] != escape)
            { throw SqlSyntax.Invalid(); }
            var low = Point(raw, ref index, budget, interval);
            if (low > char.MaxValue || !Rune.TryCreate((char)value, (char)low, out var pair))
            { throw SqlSyntax.Invalid(); }
            return pair;
        }
        if (value > int.MaxValue || !Rune.TryCreate((int)value, out var rune))
        { throw SqlSyntax.Invalid(); }
        return rune;
    }

    private static uint Point(string raw, ref int index, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int interval)
    {
        var count = NarrowDigits;
        if (index < raw.Length && raw[index] == Plus)
        { count = WideDigits; index += Next; }
        return SqlEscapeStringDigits.Hex(raw, ref index, count, exact: true, budget, interval);
    }
}
