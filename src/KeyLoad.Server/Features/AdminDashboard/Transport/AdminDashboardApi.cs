using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class AdminDashboardApi
{
    internal static void Map(WebApplication app)
    {
        app.MapGet(AdminDashboardProtocol.SnapshotPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AdminDashboard)));
        app.MapPost(AdminDashboardProtocol.ResourcesPath, (AdminResourcesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AdminResources, request));
        app.MapPost(AdminDashboardProtocol.QueuePath, (AdminQueueRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AdminQueue, request));
    }
}
