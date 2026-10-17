using ManagedCode.Communication.AspNetCore;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventFeedTailApi
{
    internal static IResult Submit(HttpContext context, EventFeedControlRequest request)
        => CqrsStreamHttpResults.ServerSentEvents(EventFeedTailPublicStream.AdmitAsync(context,
            EventFeedTailPublicStream.Create(context, request), context.RequestAborted));
}
