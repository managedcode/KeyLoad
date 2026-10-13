using System.Buffers;
using System.Text;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlEscapeStringCodec
{
    private const int Previous = 1;
    private const int RuneWidth = 4;
    private const int First = 0;
    private const int ByteMask = 255;
    private const char Backspace = 'b';
    private const char FormFeed = 'f';
    private const char Newline = 'n';
    private const char Return = 'r';
    private const char Tab = 't';
    private const char Hex = 'x';
    private const char Unicode = 'u';
    private const char WideUnicode = 'U';
    private const char OctalFirst = '0';
    private const char OctalLast = '7';
    private const char BackspaceValue = '\b';
    private const char FormFeedValue = '\f';
    private const char NewlineValue = '\n';
    private const char ReturnValue = '\r';
    private const char TabValue = '\t';

    internal static void Escape(string sql, ref int index, ArrayBufferWriter<byte> output,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int interval)
    {
        if (index >= sql.Length)
        { throw SqlSyntax.Invalid(); }
        var current = sql[index++];
        if (current is >= OctalFirst and <= OctalLast || current == Hex
            && index < sql.Length && char.IsAsciiHexDigit(sql[index]))
        {
            var value = current == Hex
                ? SqlEscapeStringDigits.Hex(sql, ref index, SqlEscapeStringDigits.ByteDigits, exact: false, budget, interval)
                : SqlEscapeStringDigits.Octal(sql, ref index, current, budget, interval);
            output.GetSpan(Previous)[First] = (byte)(value & ByteMask);
            output.Advance(Previous);
            return;
        }
        if (current is Unicode or WideUnicode)
        { Write(SqlEscapeStringDigits.Unicode(sql, ref index, current == WideUnicode, budget, interval), output); return; }
        var decoded = current switch
        {
            Backspace => BackspaceValue,
            FormFeed => FormFeedValue,
            Newline => NewlineValue,
            Return => ReturnValue,
            Tab => TabValue,
            _ => current
        };
        if (decoded != current)
        { Write(new Rune(decoded), output); }
        else
        { Literal(sql, ref index, output); }
    }

    internal static void Literal(string sql, ref int index, ArrayBufferWriter<byte> output)
    {
        var start = index - Previous;
        if (Rune.DecodeFromUtf16(sql.AsSpan(start), out var rune, out var consumed) != OperationStatus.Done)
        { throw SqlSyntax.Invalid(); }
        index = start + consumed;
        Write(rune, output);
    }

    internal static void Write(Rune rune, ArrayBufferWriter<byte> output)
    { output.Advance(rune.EncodeToUtf8(output.GetSpan(RuneWidth))); }
}
