namespace KeyLoad.Core;

internal enum EventVectorControlAction
{
    Open = 0,
    AdmitSource = 1,
    ObserveSource = 2,
    PublishOpen = 3,
    Offer = 4,
    PublishAcknowledgement = 5,
    PublishRefresh = 6,
    CompleteRelease = 7,
    AdmitFirstIntent = 8,
    CaptureFirstPhase = 9,
    ReserveFirstDispatch = 10,
    AbandonFirstIssuance = 11,
    CleanupAbandonedIssuance = 12
}
