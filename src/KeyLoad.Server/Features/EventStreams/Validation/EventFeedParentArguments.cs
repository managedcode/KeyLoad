namespace KeyLoad.Server.Features.EventStreams;

internal static class EventFeedParentArguments
{
    private const string WrongAction = "The event feed operation does not match its owning public route.";
    internal static Guid Open(EventFeedControlRequest request) => Require(request, EventFeedControlAction.Open);
    internal static Guid Offer(EventFeedControlRequest request) => Require(request, EventFeedControlAction.Offer);
    internal static Guid Refresh(EventFeedControlRequest request) => Require(request, EventFeedControlAction.Refresh);
    internal static Guid Acknowledge(EventFeedControlRequest request) => Require(request, EventFeedControlAction.Acknowledge);
    internal static Guid Release(EventFeedControlRequest request) => Require(request, EventFeedControlAction.Release);

    internal static Guid Require(EventFeedControlRequest request, EventFeedControlAction expected)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != expected)
        { throw Errors.Fail(ErrorCode.Validation, WrongAction); }
        return request.CommandId;
    }
}
