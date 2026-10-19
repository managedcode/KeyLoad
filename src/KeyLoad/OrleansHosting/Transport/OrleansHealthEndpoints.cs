namespace KeyLoad.Server;

internal static class OrleansHealthEndpoints
{
    private const string SiloHealthRoute = "/health/silo";
    private const string MembershipHealthRoute = "/health/membership-ready";

    internal static void Map(WebApplication app)
    {
        app.MapGet(SiloHealthRoute, (OrleansNode node) => Results.StatusCode(node.SiloJoined
            ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable));
        app.MapGet(MembershipHealthRoute, (IClusterMembershipService membership) =>
        {
            var snapshot = membership.CurrentSnapshot;
            return Results.Ok(new
            {
                Version = snapshot.Version.Value,
                ActiveSilos = snapshot.Members.Values.Where(member => member.Status == SiloStatus.Active)
                    .Select(member => member.Name).Order(StringComparer.Ordinal).ToArray()
            });
        });
    }
}
