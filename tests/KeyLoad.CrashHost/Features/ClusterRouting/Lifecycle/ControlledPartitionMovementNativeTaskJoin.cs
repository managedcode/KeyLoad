using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeTaskJoin
{
    private const int NoObservationFailures = 0;

    internal static async Task JoinObservedAsync(Task original, List<Exception> failures,
        Func<int> readNode, ControlledNativeColdShutdownEvidence.Stage stage, int originalJoinStageOffset,
        Func<Task> ownerState, CancellationToken token)
    {
        var observation = new List<Exception>();
        await ControlledNativeColdShutdownEvidence.ObserveAsync(() => original, observation,
            readNode(), stage, true, token, CancellationToken.None).ConfigureAwait(false);
        failures.AddRange(observation);
        if (observation.Count == NoObservationFailures)
        {
            return;
        }
        if (stage == ControlledNativeColdShutdownEvidence.Stage.NodeStopObservation)
        {
            await ServerFailureObserver.ObserveAsync(ownerState, failures).ConfigureAwait(false);
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
