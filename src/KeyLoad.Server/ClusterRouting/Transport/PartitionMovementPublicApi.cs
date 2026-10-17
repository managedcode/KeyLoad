namespace KeyLoad.Server;

/// <summary>Routes one current typed movement parent through the canonical signed unique request actor.</summary>
internal static class PartitionMovementPublicApi
{
    internal static void Map(WebApplication app)
        => app.MapPost(PartitionMovePublicProtocol.Route, (PartitionMoveRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MovePartition, request.MoveId, request));
}
