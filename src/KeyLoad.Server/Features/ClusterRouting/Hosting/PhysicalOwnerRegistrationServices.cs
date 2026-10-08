using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationServices
{
    internal static void Add(IServiceCollection services)
    {
        services.AddSingleton<PhysicalOwnerProbeWorkOwner>();
        services.AddSingleton<PhysicalOwnerStartupRequests>();
        services.AddSingleton<PhysicalOwnerProbeReceiver>();
        services.AddSingleton<PhysicalOwnerProbeEndpoint>();
    }

    internal static void Map(WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<NodeOptions>>().Value.MembershipAuthority;
        if (!settings.RegisterPhysicalOwners || settings.Mode != MembershipAuthoritySettingsProtocol.Proxy)
        { return; }
        app.MapPost(PhysicalOwnerProbeProtocol.Path,
            static (HttpContext context, PhysicalOwnerProbeEndpoint endpoint) => endpoint.HandleAsync(context));
    }
}
