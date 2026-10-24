namespace KeyLoad.IntegrationTests.OrleansExecution;

internal readonly record struct RequestCqrsDrainObservationSnapshot(
    bool Started,
    bool? TokenCanceledAtStart,
    TaskStatus? StartNode1,
    TaskStatus? StartNode2,
    TaskStatus? StartNode3,
    bool? TokenCanceledAtFailure,
    TaskStatus? FailureNode1,
    TaskStatus? FailureNode2,
    TaskStatus? FailureNode3,
    bool? Returned,
    bool ForcedCancellationEntered,
    bool? CaptureCanceledAtForcedCancellation,
    bool? OriginalJoined,
    TaskStatus? JoinedNode1,
    TaskStatus? JoinedNode2,
    TaskStatus? JoinedNode3,
    bool? CaptureCanceledAfterJoin);
