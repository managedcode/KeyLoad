namespace KeyLoad.UnitTests.Messaging;

internal enum QueueLifecycleCeilingFault
{
    RedriveStateVersion,
    RedriveDeliveryGeneration,
    RedriveLeaseVersion,
    RedriveReadySequence,
    RedriveOrderSequence,
    CancelStateVersion,
    CancelDeliveryGeneration,
    CancelLeaseVersion,
    ParkStateVersion,
    ParkSequence
}
