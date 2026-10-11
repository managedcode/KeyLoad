using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorSourceCapacityLedger
{
    private const int Version = 1;
    private const long EmptyCount = 0;
    private const int ExistenceProbeRecords = 1;
    private const string InvalidCapacity = "The native event vector source capacity or pin inventory is inconsistent.";

    internal static EventVectorEncodedRow Prepare(IAtomicTransaction transaction, PartitionRef partition,
        long slotDelta, long byteDelta, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(admission);
        var key = EventVectorKeys.SourceCapacity(partition);
        var current = EventVectorRowStorage.Read<EventVectorSourceCapacity>(transaction, key, admission);
        if (current is null)
        {
            RequireNoPins(transaction, partition);
            current = new EventVectorSourceCapacity
            { Version = Version, SourcePartition = partition, OpenPins = EmptyCount, RetainedBytes = EmptyCount };
        }
        if (current.Version != Version || current.SourcePartition != partition)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCapacity); }
        admission.Require(current.OpenPins, current.RetainedBytes, slotDelta, byteDelta);
        var changed = current with
        {
            OpenPins = checked(current.OpenPins + slotDelta),
            RetainedBytes = checked(current.RetainedBytes + byteDelta)
        };
        return EventVectorEncodedRow.Create(key, changed, admission);
    }

    private static void RequireNoPins(IKeyValueView view, PartitionRef partition)
    {
        var found = false;
        view.VisitRange(EventVectorKeys.Pins(partition), ExistenceProbeRecords,
            (_, _) => { found = true; return false; });
        if (found)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCapacity); }
    }
}
