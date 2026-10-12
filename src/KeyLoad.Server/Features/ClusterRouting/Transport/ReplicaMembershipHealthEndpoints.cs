using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipHealthEndpoints
{
    private const string RegistrationHealthRoute = "/health/physical-owner-registration";
    private const string SiloHealthRoute = "/health/silo";
    private const string MapHealthMembershipReadyRoute = "/health/membership-ready";

    internal static void Map(WebApplication app)
    {
        app.MapGet(SiloHealthRoute, Silo);
        app.MapGet(RegistrationHealthRoute, Registration);
        app.MapGet(MapHealthMembershipReadyRoute, MembershipAsync);
    }

    private static IResult Registration(OrleansNode node, IOptions<NodeOptions> options)
        => Status(options.Value.PhysicalGroupTrust.RegisterPhysicalOwners
            && options.Value.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Control
            && node.OwnerRegistration is { Verified: true });

    private static IResult Silo(OrleansNode node)
        => Status(node.SiloJoined && node.Grains is not null);

    private static IResult Status(bool ready)
        => Results.StatusCode(ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> MembershipAsync(OrleansNode node, CancellationToken token)
    {
        var readiness = await node.MembershipReadyAsync(token).ConfigureAwait(false);
        return readiness is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Json(readiness);
    }
}
