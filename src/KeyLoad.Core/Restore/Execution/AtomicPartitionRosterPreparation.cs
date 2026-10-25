using KeyLoad.Storage;

namespace KeyLoad.Core.BackupRestore.Execution;

/// <summary>Preserves roster publication context while the execution provider owns a discard-only child callback.</summary>
internal static class AtomicPartitionRosterPreparation
{
    internal static T Prepare<T>(IAtomicTransaction inner,
        long storePosition, long appliedIndex, Func<IAtomicTransaction, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        return inner.Prepare(child => prepare(new AtomicPartitionRosterTransaction(child,
            storePosition, appliedIndex)));
    }
}
