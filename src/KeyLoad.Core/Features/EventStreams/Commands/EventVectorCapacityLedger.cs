using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorCapacityLedger
{
    private const int Version = 1;
    private const long EmptyCount = 0;
    private const int ExistenceProbeRecords = 1;
    private const string InvalidCapacity = "The native event vector capacity or owned inventory is inconsistent.";

    internal static EventVectorEncodedRow Prepare(IAtomicTransaction transaction, PartitionRef partition,
        long slotDelta, long byteDelta, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(admission);
        var key = EventVectorKeys.Capacity(partition);
        var current = EventVectorRowStorage.Read<EventVectorCapacity>(transaction, key, admission);
        if (current is null)
        {
            RequireNoOwnedRows(transaction, partition);
            current = new EventVectorCapacity
            {
                Version = Version,
                ControlPartition = partition,
                OpenMaps = EmptyCount,
                RetainedBytes = EmptyCount
            };
        }
        if (current.Version != Version || current.ControlPartition != partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCapacity);
        }
        admission.Require(current.OpenMaps, current.RetainedBytes, slotDelta, byteDelta);
        var changed = current with
        {
            OpenMaps = checked(current.OpenMaps + slotDelta),
            RetainedBytes = checked(current.RetainedBytes + byteDelta)
        };
        return EventVectorEncodedRow.Create(key, changed, admission);
    }

    private static void RequireNoOwnedRows(IKeyValueView view, PartitionRef partition)
    {
        foreach (var prefix in new[] { EventVectorKeys.Maps(partition), EventVectorKeys.Pages(partition),
            EventVectorKeys.Offers(partition), EventVectorKeys.Phases(partition), EventVectorKeys.Proofs(partition),
            EventVectorKeys.ParentCalls(partition), EventVectorKeys.CleanupCalls(partition),
            EventVectorKeys.ActiveFirstIssuances(partition) })
        {
            var found = false;
            view.VisitRange(prefix, ExistenceProbeRecords, (_, _) => { found = true; return false; });
            if (found)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidCapacity);
            }
        }
    }
}
