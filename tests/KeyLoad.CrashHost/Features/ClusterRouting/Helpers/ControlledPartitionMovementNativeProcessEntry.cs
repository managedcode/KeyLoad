using KeyLoad.Replication;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Independent actual committed-entry identity under original native query work limits.</summary>
internal static class ControlledPartitionMovementNativeProcessEntry
{
    private const long FirstIndex = 1;
    private const int NoExaminedRecords = 0;
    private const string Exhausted = "The original movement entry inspection exhausted its record bound.";
    private const string Duplicate = "The original movement command has multiple committed entries.";
    private const string Missing = "The original movement entry is not retained in the committed tail.";

    internal static ReplicaEntry Read(ControlledPartitionMovementNativeHost owner, Guid commandId,
        CancellationToken cancellationToken)
    {
        var state = owner.Log.State;
        var work = new global::KeyLoad.Core.ReadExecutionBudget(owner.Runtime.Core.DatabaseLimits, owner.Clock, cancellationToken);
        ReplicaEntry? found = null;
        var examined = NoExaminedRecords;
        for (var index = FirstIndex; index <= state.CommittedIndex; index++)
        {
            work.Check();
            if (examined >= owner.Database.Limits.MaxScanRecords)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted); }
            examined++;
            var entry = owner.Log.ReadEntry(index, work.ChargeBytes);
            if (entry?.Operation?.Id != commandId)
            { continue; }
            if (found is not null)
            { throw new InvalidOperationException(Duplicate); }
            found = entry;
        }
        work.Check();
        return found ?? throw new InvalidOperationException(Missing);
    }
}
