using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree.ResourceExecution;

/// <summary>Owns the store gate protocol: readers share the gate, one writer at a time compiles and flushes the journal
/// under the upgradeable lock while readers continue, and the writer holds the exclusive lock only while it applies.</summary>
internal static class ZoneTreePhaseGate
{
    internal static void EnterRead(ZoneTreeStoreRuntime runtime)
        => Enter(runtime.Gate.EnterReadLock, DatabasePhaseKind.ProviderReadGateWait);

    internal static long EnterCommit(ZoneTreeStoreRuntime runtime)
    {
        Enter(runtime.Gate.EnterUpgradeableReadLock, DatabasePhaseKind.ProviderWriteGateWait);
        return DatabasePhaseTelemetry.Begin();
    }

    internal static void EnterApply(ZoneTreeStoreRuntime runtime)
        => Enter(runtime.Gate.EnterWriteLock, DatabasePhaseKind.ProviderApplyGateWait);

    internal static void ExitApply(ZoneTreeStoreRuntime runtime) => runtime.Gate.ExitWriteLock();

    internal static void ExitCommit(ZoneTreeStoreRuntime runtime, long started, DatabasePhaseOutcome outcome)
    {
        try
        {
            runtime.Gate.ExitUpgradeableReadLock();
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateHold, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateHold, outcome, started);
    }

    private static void Enter(Action enter, DatabasePhaseKind phase)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            enter();
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(phase, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(phase, DatabasePhaseOutcome.Completed, started);
    }
}
