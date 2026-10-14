using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledNativeColdShutdownEvidence
{
    internal enum Stage
    {
        GroupObservation, GroupOriginalJoin, NodeStopObservation, NodeStopOriginalJoin,
        NodeDisposal, HttpStop, ApplicationDisposalObservation, ApplicationDisposalOriginalJoin,
        PartitionDisposalObservation, PartitionDisposalOriginalJoin
    }
    private const string Node = "KeyLoad.NativeCold.Node";
    private const string Phase = "KeyLoad.NativeCold.Stage";
    private const string Present = "KeyLoad.NativeCold.OriginalTaskPresent";
    private const string TaskIdentity = "KeyLoad.NativeCold.OriginalTaskId";
    private const string Before = "KeyLoad.NativeCold.OriginalStatusAtCapture";
    private const string After = "KeyLoad.NativeCold.OriginalStatusAfterJoin";
    private const string ObservationRequested = "KeyLoad.NativeCold.ObservationRequested";
    private const string CallerRequested = "KeyLoad.NativeCold.CallerRequested";
    private const string HasCancellation = "KeyLoad.NativeCold.IsCancellation";
    private const string ObservationMatches = "KeyLoad.NativeCold.MatchesObservationToken";
    private const string CallerMatches = "KeyLoad.NativeCold.MatchesCallerToken";
    private const string Prefix = "KEYLOAD_NATIVE_COLD";
    private const string OwnerStatePrefix = "KEYLOAD_NATIVE_COLD_OWNER_STATE";
    private const string JoinedRequestWorkField = " joinedRequestWork=";
    private const string DiscoveryPresentField = " discoveryPresent=";
    private const string NodeField = " node=";
    private const string StageField = " stage=";
    private const string PhaseField = " phase=";
    private const string TaskField = " task=";
    private const string StatusField = " status=";
    private const string ObserverRequestedField = " observerRequested=";
    private const string CallerRequestedField = " callerRequested=";
    private const string CanceledField = " canceled=";
    private const string MatchesObserverField = " matchesObserver=";
    private const string MatchesCallerField = " matchesCaller=";
    private const int MissingTaskId = 0;
    private const int MissingTaskStatus = -1;
    private const int CapturePhase = 0;
    private const int JoinedPhase = 1;

    internal static async Task ObserveAsync(Func<Task> begin, List<Exception> failures,
        int node, Stage stage, bool boundedWait, CancellationToken observation, CancellationToken caller)
    {
        Task? original = null;
        var captured = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            original = begin();
            return boundedWait ? original.WaitAsync(observation) : original;
        }, captured).ConfigureAwait(false);
        failures.AddRange(captured);
        var taskId = original?.Id ?? MissingTaskId;
        var status = original is null ? MissingTaskStatus : (int)original.Status;
        var observerRequested = observation.IsCancellationRequested;
        var callerRequested = caller.IsCancellationRequested;
        foreach (var error in captured)
        {
            await ServerFailureObserver.ObserveAsync(() => CaptureAsync(error, node, stage,
                taskId, status, observerRequested, callerRequested, observation, caller), failures).ConfigureAwait(false);
        }
    }

    internal static async Task AfterJoinAsync(IReadOnlyList<Exception> observations, Task original,
        List<Exception> failures, int node, Stage observedStage)
    {
        foreach (var error in observations)
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                var status = (int)original.Status;
                if (error.Data[Phase] is int captured && captured == (int)observedStage
                    && error.Data[TaskIdentity] is int capturedTask && capturedTask == original.Id)
                {
                    error.Data[After] = status;
                }
                return Console.Error.WriteLineAsync(FormattableString.Invariant($"{Prefix}{NodeField}{node}{StageField}{(int)observedStage}{PhaseField}{JoinedPhase}{TaskField}{original.Id}{StatusField}{status}"));
            }, failures).ConfigureAwait(false);
        }
    }

    private static Task CaptureAsync(Exception error, int node, Stage stage,
        int taskId, int status, bool observerRequested, bool callerRequested,
        CancellationToken observation, CancellationToken caller)
    {
        var canceled = error as OperationCanceledException;
        var matchesObserver = canceled is not null && canceled.CancellationToken == observation;
        var matchesCaller = canceled is not null && canceled.CancellationToken == caller;
        if (!error.Data.Contains(Phase))
        {
            error.Data[Node] = node;
            error.Data[Phase] = (int)stage;
            error.Data[Present] = taskId != MissingTaskId;
            error.Data[TaskIdentity] = taskId;
            error.Data[Before] = status;
            error.Data[ObservationRequested] = observerRequested;
            error.Data[CallerRequested] = callerRequested;
            error.Data[HasCancellation] = canceled is not null;
            error.Data[ObservationMatches] = matchesObserver;
            error.Data[CallerMatches] = matchesCaller;
        }
        return Console.Error.WriteLineAsync(FormattableString.Invariant($"{Prefix}{NodeField}{node}{StageField}{(int)stage}{PhaseField}{CapturePhase}{TaskField}{taskId}{StatusField}{status}{ObserverRequestedField}{observerRequested}{CallerRequestedField}{callerRequested}{CanceledField}{canceled is not null}{MatchesObserverField}{matchesObserver}{MatchesCallerField}{matchesCaller}"));
    }
    internal static Task CaptureNodeStopOwnerStateAsync(int node, Task original,
        bool joinedRequestWork, bool discoveryPresent)
        => Console.Error.WriteLineAsync(FormattableString.Invariant($"{OwnerStatePrefix}{NodeField}{node}{TaskField}{original.Id}{StatusField}{(int)original.Status}{JoinedRequestWorkField}{joinedRequestWork}{DiscoveryPresentField}{discoveryPresent}"));

}
