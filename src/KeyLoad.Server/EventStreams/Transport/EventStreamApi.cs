using KeyLoad.Clustering;
using KeyLoad.Server.Features.EventStreams;

namespace KeyLoad.Server;

internal static class EventStreamApi
{
    private const string StreamReadPath = "/v1/streams/read";
    private const string EventReadPath = "/v1/events/read";
    private const string ConfigurePath = "/v1/subscriptions/configure";
    private const string SeekPath = "/v1/subscriptions/seek";
    private const string PausePath = "/v1/subscriptions/pause";
    private const string ReceivePath = "/v1/subscriptions/receive";
    private const string DeliveryPath = "/v1/subscriptions/delivery";
    private const string ProcessPath = "/v1/subscriptions/process";
    private const string StatusPath = "/v1/subscriptions/status";

    internal static void Map(WebApplication app)
    {
        app.MapPost(EventFeedTailPublicProtocol.Route, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedTailApi.Submit(context, request));
        app.MapPost(EventFeedOpenProtocol.Route, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedParentApi.SubmitAsync(context, request, EventFeedControlAction.Open));
        app.MapPost(EventFeedOfferProtocol.Route, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedParentApi.SubmitAsync(context, request, EventFeedControlAction.Offer));
        app.MapPost(EventFeedRefreshProtocol.Route, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedParentApi.SubmitAsync(context, request, EventFeedControlAction.Refresh));
        app.MapPost(EventFeedSettlementProtocol.AcknowledgeRoute, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedParentApi.SubmitAsync(context, request, EventFeedControlAction.Acknowledge));
        app.MapPost(EventFeedSettlementProtocol.ReleaseRoute, (EventFeedControlRequest request, HttpContext context) =>
            EventFeedParentApi.SubmitAsync(context, request, EventFeedControlAction.Release));
        app.MapPost(StreamReadPath, (ReadStreamRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Stream, request));
        app.MapPost(EventReadPath, (ReadEventSourceRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.EventSource, request));
        app.MapPost(AggregateReplayProtocol.Route, (ReadAggregateReplayRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AggregateReplay, request));
        app.MapPost(ConfigurePath, (ConfigureSubscriptionRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigureSubscription, request.CommandId, request));
        app.MapPost(SeekPath, (SeekSubscriptionRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SeekSubscription, request.CommandId, request));
        app.MapPost(PausePath, (SetSubscriptionPausedRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SetSubscriptionPaused, request.CommandId, request));
        app.MapPost(ReceivePath, (ReceiveSubscriptionRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ReceiveSubscription, request.RequestId, request));
        app.MapPost(DeliveryPath, (SubscriptionDeliveryCommand request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SubscriptionDelivery, request.CommandId, request));
        app.MapPost(ProcessPath, (SubscriptionProcessingRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SubscriptionProcessing, request.CommandId, request));
        app.MapPost(StatusPath, (GetSubscriptionRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Subscription, request));
    }
}
