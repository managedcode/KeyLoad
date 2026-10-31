using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextOnlineReaderLedger
{

    internal static Dictionary<TextSelectedProjectionLease,
        (TextOnlineGenerationPin Pin, TextResourceReservation Grant, bool IndexExited)> Admit(
        Dictionary<TextSelectedProjectionLease,
            (TextOnlineGenerationPin Pin, TextResourceReservation Grant, bool IndexExited)>? readers,
        ReadExecutionBudget budget, TextSelectedProjectionLease reader)
    {
        budget.Check();
        if (readers?.ContainsKey(reader) == true)
        {
            throw TextErrors.Ownership();
        }
        if (readers is not null)
        {
            return readers;
        }
        return new(ReferenceEqualityComparer.Instance);
    }
}
