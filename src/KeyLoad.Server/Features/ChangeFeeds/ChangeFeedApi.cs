using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class ChangeFeedApi
{
    private const string ReadPath = "/v1/changes/read";
    private const string OutboxPath = "/v1/admin/outbox/status";
    private const string PurgePath = "/v1/admin/outbox/purge";
    private const string ConfigureProjectionPath = "/v1/admin/projections/configure";
    private const string ReadProjectionPath = "/v1/admin/projections/read";
    private const string CommitProjectionPath = "/v1/admin/projections/commit";
    private const string ReleaseProjectionPath = "/v1/admin/projections/release";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ReadPath, (ReadChangeFeedRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.ChangeFeed, request));
        app.MapPost(OutboxPath, (GetOutboxStatusRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.OutboxStatus, request));
        app.MapPost(PurgePath, (PurgeOutboxRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.PurgeOutbox, request.CommandId, request));
        app.MapPost(ConfigureProjectionPath, (ConfigureProjectionConsumerRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigureProjectionConsumer, request.CommandId, request));
        app.MapPost(ReadProjectionPath, (ReadProjectionBatchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.ProjectionBatch, request));
        app.MapPost(CommitProjectionPath, (CommitProjectionBatchRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.CommitProjectionBatch, request.CommandId, request));
        app.MapPost(ReleaseProjectionPath, (ReleaseProjectionConsumerRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ReleaseProjectionConsumer, request.CommandId, request));
    }
}
