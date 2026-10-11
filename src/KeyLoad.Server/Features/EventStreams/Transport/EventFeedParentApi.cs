namespace KeyLoad.Server.Features.EventStreams;

internal static class EventFeedParentApi
{
    internal static Task<IResult> SubmitAsync(HttpContext context, EventFeedControlRequest request,
        EventFeedControlAction expected)
    {
        var commandId = EventFeedParentArguments.Require(request, expected);
        return ApiGrainDispatch.SubmitAsync(context, OperationKind.EventFeedControl, commandId, request);
    }
}
