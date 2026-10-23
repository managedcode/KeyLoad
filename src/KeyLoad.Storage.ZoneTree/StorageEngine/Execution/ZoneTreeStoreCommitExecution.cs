using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Compiles and publishes one atomic transaction under the original runtime commit gate.</summary>
internal static class ZoneTreeStoreCommitExecution
{
    internal static T Commit<T>(ZoneTreeStoreRuntime runtime, Func<IAtomicTransaction, long, T> compile)
    {
        ArgumentNullException.ThrowIfNull(compile);
        var holdStarted = ZoneTreePhaseGate.EnterCommit(runtime);
        var holdOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            runtime.Check();
            var tx = new ZoneTreeTransaction(runtime);
            var nextPosition = checked(runtime.Position + 1);
            T result;
            try
            {
                result = compile(tx, nextPosition);
            }
            finally
            {
                tx.EndPreparationContext();
            }
            var changes = tx.PrepareChanges();
            if (changes.Length == 0)
            {
                holdOutcome = DatabasePhaseOutcome.Completed;
                return result;
            }

            ZoneTreeJournalPublication.Publish(runtime, tx, changes, nextPosition);
            holdOutcome = DatabasePhaseOutcome.Completed;
            return result;
        }
        finally
        {
            ZoneTreePhaseGate.ExitCommit(runtime, holdStarted, holdOutcome);
        }
    }
}
