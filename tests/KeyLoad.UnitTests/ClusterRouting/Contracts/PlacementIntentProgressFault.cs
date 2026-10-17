namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum PlacementIntentProgressFault
{
    OtherRequest,
    OtherIntent,
    RepeatedMove,
    EmptyMove,
    RepeatedRevision,
    RepeatedCompletedCount,
    ChangedTotal,
    NonRetiredPhase,
    MixedPayload,
    WrongSequence,
    WrongMessage
}
