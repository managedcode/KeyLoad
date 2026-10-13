using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class TenantPlacementPublicApi
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(TenantPlacementPublicProtocol.ConfigureRoute,
            (ConfigurePlacementPolicyRequest request, HttpContext context) =>
                ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigurePlacementPolicy, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(TenantPlacementPublicProtocol.PolicyRoute,
            (TenantPlacementScope request, HttpContext context) =>
                ApiGrainDispatch.ReadAsync(context, GrainReadKind.PlacementPolicy, request));
        app.MapPost(TenantPlacementPublicProtocol.PlanRoute,
            (TenantPlacementPlanRequest request, HttpContext context) =>
                ApiGrainDispatch.ReadAsync(context, GrainReadKind.PlacementPlan, request));
        app.MapPost(TenantPlacementPublicProtocol.PersistRoute,
            (PersistPlacementIntentRequest request, HttpContext context) =>
                ApiGrainDispatch.SubmitAsync(context, OperationKind.PersistPlacementIntent, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(TenantPlacementPublicProtocol.ApplyRoute,
            (ApplyPlacementIntentRequest request, HttpContext context) =>
                ApiGrainDispatch.SubmitAsync(context, OperationKind.ApplyPlacementIntent, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(TenantPlacementPublicProtocol.IntentRoute,
            (PlacementIntentReadRequest request, HttpContext context) =>
                ApiGrainDispatch.ReadAsync(context, GrainReadKind.PlacementIntent, request));
    }
}
