namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativePrepareDeltaWitness
{
    private const long NextEntry = 1;
    private const int MaximumCandidateRecords = 1;
    private const string ScanExceeded = "The original movement entry inspection exhausted its record bound.";
    private const long UnavailableTerm = -1;
    private const int UnavailableKind = -1;
    private const string Prefix = "KEYLOAD_NATIVE_PREPARE_DELTA";

    internal static async Task ObserveAsync(
        global::KeyLoad.CrashHost.Features.ClusterRouting.ControlledPartitionMovementNativeHost source,
        long beforePosition, long beforeIndex, Guid originalCommand, Guid changedCommand,
        List<Exception> failures, CancellationToken token)
    {
        var afterPosition = source.Store.Position;
        var captured = source.Log.State;
        var index = checked(beforeIndex + NextEntry);
        var hasCommittedCandidate = index <= captured.CommittedIndex;
        if (!hasCommittedCandidate)
        {
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => WriteAsync(
                beforePosition, afterPosition, beforeIndex, captured.LastIndex, captured.CommittedIndex,
                index, UnavailableTerm, false, UnavailableTerm, UnavailableKind, false, false, false, originalCommand == changedCommand), failures);
            return;
        }
        var beforeObservation = failures.Count;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => source.Partition.Coordinator.ReadBarrierAsync(token), failures);
        if (failures.Count != beforeObservation)
        { return; }
        var work = new global::KeyLoad.Core.ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        work.Check();
        if (source.Database.Limits.MaxScanRecords < MaximumCandidateRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScanExceeded); }
        var entry = source.Log.ReadEntry(index, work.ChargeBytes);
        work.Check();
        var operation = entry?.Operation;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => WriteAsync(
            beforePosition, afterPosition, beforeIndex, captured.LastIndex, captured.CommittedIndex,
            index, entry?.Index ?? UnavailableTerm, entry is not null, entry?.Term ?? UnavailableTerm,
            operation is null ? UnavailableKind : (int)operation.Kind, operation is not null,
            operation?.Id == originalCommand, operation?.Id == changedCommand, originalCommand == changedCommand), failures);
    }

    private static Task WriteAsync(long beforePosition, long afterPosition, long beforeIndex, long afterIndex,
        long committedIndex, long requestedIndex, long actualIndex, bool available, long term, int kind, bool hasOperation,
        bool originalIdEqual, bool changedIdEqual, bool requestIdsEqual)
        => Console.Error.WriteLineAsync(FormattableString.Invariant(
            $"{Prefix} positionBefore={beforePosition} positionAfter={afterPosition} indexBefore={beforeIndex} indexAfter={afterIndex} committed={committedIndex} requestedEntry={requestedIndex} entry={actualIndex} available={available} term={term} kind={kind} hasOperation={hasOperation} originalIdEqual={originalIdEqual} changedIdEqual={changedIdEqual} requestIdsEqual={requestIdsEqual}"));
}
