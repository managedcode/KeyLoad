using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

internal static class ZoneTreePhaseGate
{
    internal static void EnterRead(ZoneTreeStoreRuntime runtime)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            runtime.Gate.EnterReadLock();
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderReadGateWait, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderReadGateWait, DatabasePhaseOutcome.Completed, started);
    }

    internal static long EnterCommit(ZoneTreeStoreRuntime runtime)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            runtime.Gate.EnterWriteLock();
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateWait, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        var holdStarted = DatabasePhaseTelemetry.Begin();
        DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateWait, DatabasePhaseOutcome.Completed, started);
        return holdStarted;
    }

    internal static void ExitCommit(ZoneTreeStoreRuntime runtime, long started, DatabasePhaseOutcome outcome)
    {
        try
        {
            runtime.Gate.ExitWriteLock();
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateHold, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.ProviderWriteGateHold, outcome, started);
    }
}
