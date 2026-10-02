using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class MessagingApi
{
    private const string ReceivePath = "/v1/queues/receive";
    private const string DeliveryPath = "/v1/queues/delivery";
    private const string ProcessPath = "/v1/queues/process";
    private const string InspectPath = "/v1/queues/inspect";
    private const string DispatchPath = "/v1/admin/dispatch";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ReceivePath, (ReceiveRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.Receive, request.RequestId, request));
        app.MapPost(DeliveryPath, (DeliveryCommand request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.Delivery, request.CommandId, request));
        app.MapPost(ProcessPath, (ProcessingRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.Processing, request.CommandId, request));
        app.MapPost(InspectPath, (InspectMessageRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Message, request));
        app.MapPost(DispatchPath, (bool paused, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SetDispatch, ApiGrainDispatch.CommandId(context), paused));
    }
}
