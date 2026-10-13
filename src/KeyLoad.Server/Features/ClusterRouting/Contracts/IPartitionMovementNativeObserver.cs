namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Optional owner-local test observation; never part of a peer wire or authorization decision.</summary>
internal interface IPartitionMovementNativeObserver
{
    IPartitionMovementNativeObservation? Begin(Guid originalNonce, string receivingVoter);
}

internal interface IPartitionMovementNativeObservation
{
    void Enter(PartitionMovementNativeObservationStage stage);
    void Complete(ErrorCode? error, bool authenticated, bool dispatched, Exception? original,
        Exception? secondary, PartitionMovementNativeObservationStage? failureStage);
}

internal enum PartitionMovementNativeObservationStage
{
    RuntimeAdmission,
    RuntimeCompletion,
    AdmissionShape,
    OuterMac,
    CallerIdentity,
    CallerActive,
    CallerDiscovery,
    DiscoveryShape,
    DiscoveryVoter,
    DiscoveryMac,
    DiscoveryContract,
    CallerActiveAfterDiscovery,
    ExpiryReplay,
    InnerDispatch,
    OuterSigning
}
