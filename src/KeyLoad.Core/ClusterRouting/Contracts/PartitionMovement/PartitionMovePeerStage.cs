namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PeerStageAlias)]
internal enum PartitionMovePeerStage
{
    Fence = 1,
    Capture = 2,
    StagePage = 3,
    Install = 4,
    PublishWitness = 5,
    Retire = 6,
    Abort = 7,
    ControlPrepare = 8,
    ControlAdvance = 9,
    ControlFinalize = 10,
    ControlAuthorize = 11,
    ControlAcknowledge = 12,
    ControlAcceptFence = 13,
    ControlBeginAbort = 14,
    ControlFinalizeAbort = 15,
    ControlCompleteRetirement = 16,
    ControlCancelGrants = 17,
    SourceBeginAbort = 18,
    ControlAdmitCommand = 19,
    ControlAcknowledgeCommand = 20,
    ControlFinalizeCommand = 21,
    ControlApplyCommand = 22,
    ControlCheckpoint = 23,
    ReceiverIssue = 24,
    RetireCancel = 25,
}
