namespace KeyLoad.Server;

internal static class QueueDispatchApi
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(QueueDispatchControlProtocol.Route, (QueueDispatchControlRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.SetQueueDispatch, ApiGrainDispatch.CommandId(context), request));
    }
}
