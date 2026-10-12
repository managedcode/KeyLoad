using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessCapture
{
    private const long FirstIndex = 1;
    private const int FirstRecord = 0;
    private const int PageRecords = 4_096;
    private const int MaximumPages = 16;
    private const string RowSeparator = ":";

    internal static async Task<ControlledPartitionMovementNativeProcessSnapshot> ReadAsync(
        ControlledPartitionMovementNativeHost owner, CancellationToken cancellationToken)
    {
        var observedCut = owner.Log.State.CommittedIndex;
        await owner.Partition.Materializer.WaitForApplyAsync(observedCut, cancellationToken).ConfigureAwait(false);
        await owner.Log.ProtocolGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = owner.Log.State;
            if (state.CommittedIndex != observedCut || owner.Database.LastApplied != observedCut)
            { throw Errors.Fail(ErrorCode.OwnershipLost, ControlledPartitionMovementNativeProcessProtocol.Invalid); }
            var work = new ReadExecutionBudget(owner.Runtime.Core.DatabaseLimits, owner.Clock, cancellationToken);
            var entries = ImmutableArray.CreateBuilder<ReplicaEntry>();
            for (var index = FirstIndex; index <= state.CommittedIndex; index++)
            {
                work.Check();
                if (entries.Count >= owner.Database.Limits.MaxScanRecords)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, ControlledPartitionMovementNativeProcessProtocol.Invalid); }
                entries.Add(owner.Log.ReadEntry(index, work.ChargeBytes)
                    ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ControlledPartitionMovementNativeProcessProtocol.Invalid));
            }
            return owner.Store.Read<ControlledPartitionMovementNativeProcessSnapshot>(view => new(owner.Store.Identity, owner.Store.Position, state,
                Rows(work, view), entries.ToImmutable()));
        }
        finally { owner.Log.ProtocolGate.Release(); }
    }

    private static ImmutableArray<string> Rows(ReadExecutionBudget work, IKeyValueView view)
    {
        var rows = ImmutableArray.CreateBuilder<string>();
        byte[]? after = null;
        for (var page = FirstRecord; page < MaximumPages; page++)
        {
            var result = work.Scan(view, [], PageRecords, after);
            foreach (var row in result.Records)
            {
                if (after is not null && row.Key.Span.SequenceCompareTo(after) <= FirstRecord)
                { throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid); }
                rows.Add(Convert.ToHexString(row.Key.Span) + RowSeparator + Convert.ToHexString(row.Value.Span));
                after = row.Key.ToArray();
            }
            if (!result.HasMore) { return rows.ToImmutable(); }
            if (result.Records.IsEmpty)
            { throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid); }
        }
        throw Errors.Fail(ErrorCode.BudgetExceeded, ControlledPartitionMovementNativeProcessProtocol.Invalid);
    }
}
