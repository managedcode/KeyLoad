using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class ChangeFeedApi
{
    private const string ReadPath = ApiRoutes.ChangesRead;
    private const string OutboxPath = ApiRoutes.OutboxStatus;
    private const string PurgePath = ApiRoutes.OutboxPurge;
    private const string ConfigureProjectionPath = ApiRoutes.ProjectionsConfigure;
    private const string ReadProjectionPath = ApiRoutes.ProjectionsRead;
    private const string CommitProjectionPath = ApiRoutes.ProjectionsCommit;
    private const string ReleaseProjectionPath = ApiRoutes.ProjectionsRelease;

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
