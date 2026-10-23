using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

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
            return Fork(runtime, [], child => prepare(child, position));
        }
        finally
        {
            runtime.Gate.ExitReadLock();
        }
    }

    internal static T Fork<T>(ZoneTreeStoreRuntime runtime, IEnumerable<ZoneTreeStagedEntry> inherited,
        Func<IAtomicTransaction, T> prepare, bool requireCommitGate = false)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var commitHeld = runtime.Gate.IsUpgradeableReadLockHeld || runtime.Gate.IsWriteLockHeld;
        if (requireCommitGate ? !commitHeld : !runtime.Gate.IsReadLockHeld && !commitHeld)
        {
            throw new InvalidOperationException(MissingGate);
        }
        runtime.Check();
        return new ZoneTreePreparationTransaction(runtime, inherited).Run(prepare);
    }
}
