namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ParentPhaseIdentityAlias)]
internal sealed record PartitionMovementParentPhaseIdentity(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] PartitionMoveRequest OriginalTransferRequest,
    [property: global::Orleans.Id(2)] string OperatorPrincipalId,
    [property: global::Orleans.Id(3)] PartitionMovementParentPhaseRole Role,
    [property: global::Orleans.Id(4)] int Ordinal,
    [property: global::Orleans.Id(5)] long CleanupGeneration = PartitionMovementProtocol.InitialCleanupGeneration);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ParentPhaseRoleAlias)]
internal enum PartitionMovementParentPhaseRole
{
    Prepare = 1,
    FenceGrant = 2,
    Fence = 3,
    FenceAcknowledge = 4,
    AcceptFence = 5,
    CaptureGrant = 6,
    Capture = 7,
    CaptureAcknowledge = 8,
    AdvanceCaptured = 9,
    StageGrant = 10,
    StagePage = 11,
    StageAcknowledge = 12,
    InstallGrant = 13,
    Install = 14,
    InstallAcknowledge = 15,
    AdvanceInstalled = 16,
    FinalizePublication = 17,
    PublishGrant = 18,
    Publish = 19,
    PublishAcknowledge = 20,
    RetireGrant = 21,
    Retire = 22,
    RetireAcknowledge = 23,
    CompleteRetirement = 24,
    BeginAbort = 25,
    SourceClosureGrant = 26,
    SourceClosure = 27,
    SourceClosureAcknowledge = 28,
    TargetAbortGrant = 29,
    TargetAbort = 30,
    TargetAbortAcknowledge = 31,
    SourceAbortGrant = 32,
    SourceAbort = 33,
    SourceAbortAcknowledge = 34,
    CancelGrants = 35,
    FinalizeAbort = 36,
    ReceiverIssue = 37,
    RetireCancellation = 38,
    RetireCancellationObservation = 39,
}
