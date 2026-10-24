using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one execution gate cut while every preparation mutation stays in its child transaction.</summary>
internal static class ZoneTreeStorePreparationExecution
{
    private const int NextPosition = 1;
    internal const string OutsideTransactionCallback = "Transaction preparation requires its original synchronous compiler callback.";
    private const string Nested = "Store preparation cannot enter an already owned execution gate.";
    private const string MissingGate = "Transaction preparation requires its owning execution gate.";

    internal static T Prepare<T>(ZoneTreeStoreRuntime runtime, Func<IAtomicTransaction, long, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (runtime.Gate.IsReadLockHeld || runtime.Gate.IsWriteLockHeld || runtime.Gate.IsUpgradeableReadLockHeld)
        {
            throw new InvalidOperationException(Nested);
        }
        ZoneTreePhaseGate.EnterRead(runtime);
        try
        {
            runtime.Check();
            var position = checked(runtime.Position + NextPosition);
            return Fork(runtime, [], [], child => prepare(child, position));
        }
        finally
        {
            runtime.Gate.ExitReadLock();
        }
    }

    /// <summary>Runs discard-only preparation over the parent's staged image.</summary>
    /// <param name="runtime">The owning store runtime.</param>
    /// <param name="earlier">Entries of the parent's earlier actions, visible but outside the child's frame budget.</param>
    /// <param name="action">The parent's current action entries, which the child's frame budget includes.</param>
    /// <param name="prepare">The synchronous preparation callback.</param>
    /// <param name="requireCommitGate">Whether the caller must hold the commit gate.</param>
    internal static T Fork<T>(ZoneTreeStoreRuntime runtime, IEnumerable<ZoneTreeStagedEntry> earlier,
        IEnumerable<ZoneTreeStagedEntry> action, Func<IAtomicTransaction, T> prepare, bool requireCommitGate = false)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var commitHeld = runtime.Gate.IsUpgradeableReadLockHeld || runtime.Gate.IsWriteLockHeld;
        if (requireCommitGate ? !commitHeld : !runtime.Gate.IsReadLockHeld && !commitHeld)
        {
            throw new InvalidOperationException(MissingGate);
        }
        runtime.Check();
        return new ZoneTreePreparationTransaction(runtime, earlier, action).Run(prepare);
    }
}
