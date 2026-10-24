using System.Globalization;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.QueryExecution;

internal static class ScriptDomSlots
{
    internal static bool TryGet(Dom.MoneyLiteral literal, int count, out string canonicalName)
    {
        canonicalName = string.Empty;
        var stream = literal.ScriptTokenStream;
        if (stream is not ScriptDomCheckedTokens || literal.FirstTokenIndex < FirstIndex
            || literal.FirstTokenIndex >= stream.Count)
        {
            return false;
        }
        var text = stream[literal.FirstTokenIndex].Text;
        if (text.Length < MinimumSlotText || text[FirstIndex] != SlotPrefix
            || !int.TryParse(text.AsSpan(PrefixLength), NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || position < FirstSlot || position > count)
        {
            return false;
        }
        canonicalName = CanonicalPrefix + position.ToString(CultureInfo.InvariantCulture);
        return true;
    }
    private const int FirstIndex = 0;
    private const int PrefixLength = 1;
    private const int MinimumSlotText = 2;
    private const int FirstSlot = 1;
    private const char SlotPrefix = '$';
    private const string CanonicalPrefix = "p";
}
