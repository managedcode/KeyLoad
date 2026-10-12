using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationServices
{
    internal static void Add(IServiceCollection services)
    {
        services.AddSingleton<PhysicalOwnerProbeWorkOwner>();
        services.AddSingleton<PhysicalOwnerStartupRequests>();
        services.AddSingleton<PhysicalOwnerProbeReceiver>();
        services.AddSingleton(static provider => new PhysicalOwnerProbeNativeRuntime(
            provider.GetRequiredService<IOptions<NodeOptions>>(),
            provider.GetRequiredService<OrleansNode>(),
            provider.GetRequiredService<PartitionHost>(),
            provider.GetRequiredService<PhysicalOwnerProbeReceiver>(),
            provider.GetRequiredService<PhysicalOwnerProbeWorkOwner>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PhysicalOwnerProbeNativeRuntime>>()));
        services.AddSingleton<Func<PhysicalOwnerProbeNativeRuntime>>(
            static provider => () => provider.GetRequiredService<PhysicalOwnerProbeNativeRuntime>());
    }
}
