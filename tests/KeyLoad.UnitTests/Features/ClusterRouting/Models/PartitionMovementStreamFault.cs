namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum PartitionMovementStreamFault
{
    Unbound, OtherOperation, WrongRequest, NonePhase, RetiredPhase, AbortedPhase, UnknownPhase,
    MixedPhase, WrongSequence, WrongMessage, EventId, FinalAttached, WrongEventType
}
