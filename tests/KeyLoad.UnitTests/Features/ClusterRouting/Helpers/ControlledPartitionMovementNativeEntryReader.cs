using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Finds exactly one original entry within a captured committed tail using charged native reads.</summary>
internal static class ControlledPartitionMovementNativeEntryReader
{
    private const long FirstIndex = 1;
    private const int NoExaminedRecords = 0;

    internal static ReplicaEntry Read(ControlledPartitionMovementNativeHost owner, Guid originalCommand,
        CancellationToken cancellationToken)
    {
        var state = owner.Log.State;
        var work = new ReadExecutionBudget(owner.Runtime.Core.DatabaseLimits, owner.Clock, cancellationToken);
        ReplicaEntry? found = null;
        var examined = NoExaminedRecords;
        for (var index = FirstIndex; index <= state.CommittedIndex; index++)
        {
            work.Check();
            if (examined >= owner.Database.Limits.MaxScanRecords)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, "The original movement entry inspection exhausted its record bound."); }
            examined++;
            var entry = owner.Log.ReadEntry(index, work.ChargeBytes);
            if (entry?.Operation?.Id != originalCommand)
            { continue; }
            if (found is not null)
            { throw new InvalidOperationException("The original movement command has multiple committed entries."); }
            found = entry;
        }
        work.Check();
        return found ?? throw new InvalidOperationException("The original movement entry is not retained in the committed tail.");
    }
}
