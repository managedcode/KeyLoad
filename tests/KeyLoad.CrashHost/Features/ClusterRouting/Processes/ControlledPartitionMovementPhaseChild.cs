using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Executes only an already genuinely issued original phase under the existing native journal/materializer.</summary>
internal static class ControlledPartitionMovementPhaseChild
{
    private const int FirstMutationIndex = 0;
    private const long PositionStep = 1;

    internal static async Task RunAsync(string root, string mode,
        ControlledPartitionMovementProcessInput input, CancellationToken cancellationToken)
    {
        if (input.OriginalOperation.Kind != OperationKind.PartitionMovementPhase
            || input.BeforePosition < PositionStep || input.BeforeReplicaIndex < PositionStep
            || input.MaximumFixtureEntries is not (ControlledPartitionMovementNativeJournal.SupportingHistoryEntries
                or ControlledPartitionMovementNativeJournal.TerminalHistoryEntries)
            || input.FaultStage is not (CommitStage.HeaderWritten or CommitStage.PayloadWritten
                or CommitStage.JournalFlushed or CommitStage.MutationApplied or CommitStage.ApplyCompleted)
            || mode is not (ControlledPartitionMovementProcessProtocol.Prepare
                or ControlledPartitionMovementProcessProtocol.Fault
                or ControlledPartitionMovementProcessProtocol.Recover
                or ControlledPartitionMovementProcessProtocol.Verify))
        { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
        var boundary = new CanonicalCrashBoundary(input.FaultStage, mutationIndex: FirstMutationIndex, initiallyArmed: false)
        { Position = checked(input.BeforePosition + PositionStep) };
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var actual = new ControlledPartitionMovementNativeNode(root, input.Owner, boundary.Observe,
                input.MaximumFixtureEntries);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (mode == ControlledPartitionMovementProcessProtocol.Prepare)
                {
                    if (actual.Store.Position != input.BeforePosition
                        || actual.Journal.Log.State.LastIndex != input.BeforeReplicaIndex
                        || actual.Journal.Log.State.CommittedIndex != input.BeforeReplicaIndex)
                    { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
                    await CrashHostPause.WaitForKillAsync();
                    return;
                }
                boundary.Armed = mode == ControlledPartitionMovementProcessProtocol.Fault;
                var result = actual.Journal.Submit(input.OriginalOperation, cancellationToken);
                if (mode == ControlledPartitionMovementProcessProtocol.Fault)
                { throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid); }
                await ControlledPartitionMovementPhaseChildReplay.AssertAsync(actual, input, result,
                    cancellationToken);
                var file = mode == ControlledPartitionMovementProcessProtocol.Verify
                    ? ControlledPartitionMovementProcessProtocol.VerifiedFile
                    : ControlledPartitionMovementProcessProtocol.RecoveredFile;
                await ControlledPartitionMovementProcessFiles.WriteAsync(root, file, result, cancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
