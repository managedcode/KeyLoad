using System.Runtime.CompilerServices;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextOnlineReaderLedger
{
    private const int ReferenceLedgerCapacity = 3;

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
        budget.ChargeBytes(checked(TextIncrementalSourceProtocol.MapSlotBytes
            + (long)ReferenceLedgerCapacity * (sizeof(int)
            + Unsafe.SizeOf<(int Hash, int Next, TextSelectedProjectionLease Reader,
                TextOnlineGenerationPin Pin, TextResourceReservation Grant, bool IndexExited)>())));
        return new(ReferenceEqualityComparer.Instance);
    }
}
