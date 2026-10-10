using System.Runtime.CompilerServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineReaderLedger
{
    private const int ReferenceLedgerNativeCapacity = 3;

    internal static Dictionary<NativeTextSelectedProjectionLease,
        (NativeTextOnlineGenerationPin Pin, NativeTextResourceReservation Grant, bool IndexExited)> Admit(
        Dictionary<NativeTextSelectedProjectionLease,
            (NativeTextOnlineGenerationPin Pin, NativeTextResourceReservation Grant, bool IndexExited)>? readers,
        ReadExecutionBudget budget, NativeTextSelectedProjectionLease reader,
        IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        if (readers?.ContainsKey(reader) == true)
        { throw NativeTextErrors.Ownership(); }
        if (readers is not null)
        { return readers; }
        budget.ChargeBytes(checked(NativeTextIncrementalSourceProtocol.MapSlotBytes
            + (long)ReferenceLedgerNativeCapacity * (sizeof(int)
            + Unsafe.SizeOf<(int Hash, int Next, NativeTextSelectedProjectionLease Reader,
                NativeTextOnlineGenerationPin Pin, NativeTextResourceReservation Grant, bool IndexExited)>())));
        return new(options.Value.MaximumActiveLeases, ReferenceEqualityComparer.Instance);
    }
}
