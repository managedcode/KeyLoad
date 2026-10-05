namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipHealthEndpoints
{
    internal static void Map(WebApplication app) => app.MapGet("/health/membership-ready", MembershipAsync);

    private static async Task<IResult> MembershipAsync(OrleansNode node, CancellationToken token)
    {
        var readiness = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        return readiness is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Json(readiness);
    }
}
