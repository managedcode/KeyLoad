using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Typed HTTP route adapters through the canonical signed grain gateway.</summary>
internal static class AtomicPartitionPlacementApi
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(McpToolRoutes.AdminPartitionPlacementBind, (BindAtomicPartitionPlacementRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.BindAtomicPartitionPlacement,
                ApiGrainDispatch.CommandId(context), request));
        app.MapPost(McpToolRoutes.AdminPartitionPlacementRead, (AtomicPartitionPlacementReadRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AtomicPartitionPlacement, request));
    }
}
