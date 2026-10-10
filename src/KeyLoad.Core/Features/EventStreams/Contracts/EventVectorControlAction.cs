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
    CompleteRelease = 7
}
