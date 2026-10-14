using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorSourcePinWriter
{
    private const long NewSlot = 1;
    private const long ExistingSlot = 0;
    private const string InvalidPin = "The native event vector source pin scope or encoded row is inconsistent.";

    internal static void Write(IAtomicTransaction transaction, EventVectorEncodedRow? original,
        EventVectorSourcePin changed, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(changed.Source);
        var key = EventVectorKeys.Pin(changed.Source, changed.MapId);
        var replacement = EventVectorEncodedRow.Create(key, changed, admission);
        var previousBytes = ReadOriginalBytes(transaction, key, original, admission);
        var capacity = EventVectorSourceCapacityLedger.Prepare(transaction, changed.Source.Partition,
            original is null ? NewSlot : ExistingSlot,
            checked(replacement.EncodedBytes - previousBytes), admission);
        EventVectorRowStorage.Write(transaction, replacement);
        EventVectorRowStorage.Write(transaction, capacity);
    }

    internal static void Delete(IAtomicTransaction transaction, EventSourceRef source,
        Guid mapId, EventVectorEncodedRow original, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(admission);
        var key = EventVectorKeys.Pin(source, mapId);
        var bytes = ReadOriginalBytes(transaction, key, original, admission);
        var capacity = EventVectorSourceCapacityLedger.Prepare(transaction, source.Partition,
            -NewSlot, checked(-bytes), admission);
        transaction.Delete(key);
        EventVectorRowStorage.Write(transaction, capacity);
    }

    private static long ReadOriginalBytes(IKeyValueView view, byte[] key, EventVectorEncodedRow? original,
        EventVectorAdmissionPolicy admission)
    {
        var encodedBytes = ExistingSlot;
        var found = view.ReadValue(key, current =>
        {
            encodedBytes = checked(key.LongLength + current.Length);
            admission.RequireEncodedBytes(encodedBytes);
            if (original is not { } retained || !key.AsSpan().SequenceEqual(retained.Key.Span)
                || !current.SequenceEqual(retained.Value.Span))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidPin);
            }
        });
        if (!found && original is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidPin);
        }
        return encodedBytes;
    }
}
