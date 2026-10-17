namespace KeyLoad.Core;

internal static class EventVectorPublicTerminalResult
{
    internal static bool IsTerminal(EventVectorControlAction action) => action is
        EventVectorControlAction.PublishOpen or EventVectorControlAction.Offer
        or EventVectorControlAction.PublishAcknowledgement or EventVectorControlAction.PublishRefresh
        or EventVectorControlAction.CompleteRelease;
}
