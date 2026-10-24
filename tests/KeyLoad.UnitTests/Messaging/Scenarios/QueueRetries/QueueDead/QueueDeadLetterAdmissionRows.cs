using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueDeadLetterAdmissionRows
{
    internal static QueueDeadLetterAdmissionOwnedRow Damage(ZoneTreeStore store, QueueDeadLetterSelectedState state,
        QueueDeadLetterAdmissionFault fault)
    {
        var key = fault == QueueDeadLetterAdmissionFault.MissingWitnessMetadata
            ? QueueDeadLetterSelectedSetup.Key(state.Lane, QueueDeadLetterSelectedProtocol.Metadata, QueueDeadLetterSelectedProtocol.Original)
            : QueueDeadLetterSelectedSetup.Key(state.Lane, QueueDeadLetterSelectedProtocol.Counters);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The admission fixture must own its original row.");
        store.Commit((transaction, _) =>
        {
            if (fault is QueueDeadLetterAdmissionFault.MissingCounters or QueueDeadLetterAdmissionFault.MissingWitnessMetadata)
            {
                transaction.Delete(key);
            }
            else
            {
                var counters = OrleansSerialization.Deserialize<QueueCounters>(original);
                var damaged = fault switch
                {
                    QueueDeadLetterAdmissionFault.NegativeStoredMessages => counters with { StoredMessages = -1 },
                    QueueDeadLetterAdmissionFault.NegativeDeadLetterMessages => counters with { DeadLetterMessages = -1 },
                    QueueDeadLetterAdmissionFault.NegativeNextParkedSequence => counters with { NextParkedSequence = -1 },
                    QueueDeadLetterAdmissionFault.ExcessiveDeadLetterBytes => counters with { DeadLetterBytes = counters.StoredBytes + 1 },
                    QueueDeadLetterAdmissionFault.WitnessWithoutUsage => counters with { DeadLetterMessages = 0, DeadLetterBytes = 0 },
                    _ => throw new ArgumentOutOfRangeException(nameof(fault))
                };
                transaction.PutRecord(key, damaged);
            }
            return true;
        });
        return new(key, original);
    }

    internal static void Repair(ZoneTreeStore store, QueueDeadLetterAdmissionOwnedRow row)
        => store.Commit((transaction, _) => { transaction.Put(row.Key, row.Original); return true; });
}
