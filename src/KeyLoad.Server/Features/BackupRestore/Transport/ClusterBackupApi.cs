using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class ClusterBackupApi
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(ClusterBackupProtocol.Route, (ClusterBackupOwnerRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.ClusterBackupOwner, request));
    }
}
