using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeTaskJoin
{
    private const int NoObservationFailures = 0;
    private const int OriginalJoinStageOffset = 1;

    internal static Task JoinBudgetedAsync(Task original, List<Exception> failures,
        Func<int> readNode, ControlledNativeColdShutdownEvidence.Stage stage,
        ControlledNativeColdOwnerBudget budget, List<Exception> budgetFailures,
        OrleansNode? silo, CancellationToken token)
    {
        long? observationStarted = null;
        ServerFailureObserver.Observe(() => observationStarted = budget.Clock.GetTimestamp(), budgetFailures);
        return JoinObservedAsync(original, failures,
            readNode, stage, OriginalJoinStageOffset,
            () => ControlledNativeColdShutdownEvidence.CaptureNodeStopOwnerStateAsync(
                readNode(), original, silo?.HasJoinedRequestWork ?? true, silo?.Discovery is not null),
            () => budget.CaptureReport(readNode(), stage, original, observationStarted),
            budgetFailures, token);
    }

    internal static async Task JoinObservedAsync(Task original, List<Exception> failures,
        Func<int> readNode, ControlledNativeColdShutdownEvidence.Stage stage, int originalJoinStageOffset,
        Func<Task> ownerState, Func<Func<Task>> captureBudgetReport,
        List<Exception> budgetFailures, CancellationToken token)
    {
        var observation = new List<Exception>();
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, observation,
            readNode(), stage, true, token, CancellationToken.None).ConfigureAwait(false);
        failures.AddRange(observation);
        failures.AddRange(budgetFailures);
        budgetFailures.Clear();
        if (observation.Count == NoObservationFailures)
        {
            return;
        }
        Func<Task>? budgetReport = null;
        ServerFailureObserver.Observe(() => budgetReport = captureBudgetReport(), failures);
        if (stage == ControlledNativeColdShutdownEvidence.Stage.NodeStopObservation)
        {
            await ServerFailureObserver.ObserveAsync(ownerState, failures).ConfigureAwait(false);
        }
        if (budgetReport is not null)
        {
            await ServerFailureObserver.ObserveAsync(budgetReport, failures).ConfigureAwait(false);
        }
        var completion = new List<Exception>();
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, completion,
            readNode(), (ControlledNativeColdShutdownEvidence.Stage)((int)stage + originalJoinStageOffset), false, token, CancellationToken.None).ConfigureAwait(false);
        foreach (var error in completion)
        {
            if (!observation.Any(observed => ReferenceEquals(observed, error)))
            {
                failures.Add(error);
            }
        }
        await ControlledNativeColdShutdownEvidence.AfterJoinAsync(observation, original, failures,
            readNode(), stage).ConfigureAwait(false);
    }
}
