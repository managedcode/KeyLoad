namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlNumericDigitSeparators
{
    private const char Separator = '_';
    private const char Plus = '+';
    private const char Minus = '-';
    private const char Zero = '0';
    private const char Hex = 'x';
    private const char Binary = 'b';
    private const char Octal = 'o';
    private const char One = '1';
    private const char Seven = '7';
    private const int First = 0;
    private const int Adjacent = 1;
    private const int PrefixWidth = 2;

    internal static SqlToken Token(string raw, global::KeyLoad.Core.ReadExecutionBudget? budget, int cadence)
    {
        if (!TryNormalize(raw, budget, cadence, out var normalized))
        { throw SqlSyntax.Invalid(); }
        return new(SqlTokenKind.Number, raw, NumericText: ReferenceEquals(raw, normalized) ? null : normalized);
    }

    internal static bool TryNormalize(string text, global::KeyLoad.Core.ReadExecutionBudget? budget,
        int cadence, out string normalized)
    {
        budget?.Check();
        normalized = text;
        var separatorCount = First;
        var digits = text.Length > First && text[First] is Minus or Plus ? Adjacent : First;
        var kind = digits + Adjacent < text.Length && text[digits] == Zero
            ? char.ToLowerInvariant(text[digits + Adjacent]) : default;
        var radix = kind is Hex or Binary or Octal;
        for (var index = First; index < text.Length; index++)
        {
            if (index % cadence == First)
            { budget?.Check(); }
            if (text[index] != Separator)
            { continue; }
            var next = index + Adjacent;
            if (next >= text.Length || !Digit(text[next], radix ? kind : default)
                || !(index > First && Digit(text[index - Adjacent], radix ? kind : default)
                    || radix && index == digits + PrefixWidth))
            { return false; }
            separatorCount++;
        }
        if (separatorCount == First)
        { return true; }
        normalized = string.Create(text.Length - separatorCount, (Text: text, Budget: budget, Cadence: cadence), static (destination, state) =>
        {
            var target = First;
            for (var index = First; index < state.Text.Length; index++)
            {
                if (index % state.Cadence == First)
                { state.Budget?.Check(); }
                var value = state.Text[index];
                if (value != Separator)
                { destination[target++] = value; }
            }
        });
        return true;
    }

    private static bool Digit(char value, char kind) => kind switch
    {
        Hex => char.IsAsciiHexDigit(value),
        Binary => value is Zero or One,
        Octal => value is >= Zero and <= Seven,
        _ => char.IsAsciiDigit(value)
    };
}
