namespace KeyLoad.Server.Features.ClusterRouting;

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
