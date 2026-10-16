namespace KeyLoad.Server;

/// <summary>Maps typed table lifecycle commands through the existing authenticated connection execution.</summary>
internal static class RelationalStorageApi
{
    internal static void Map(WebApplication app)
        => app.MapPost(McpToolRoutes.TablesDrop, (DropTableRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.DropTable,
                ApiGrainDispatch.CommandId(context), request));
}
