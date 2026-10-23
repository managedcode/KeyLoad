using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<ServerRuntimeOptions>();
            var node = runtime.Node.Value;
            return new GrainRequestCodec(node.Incarnation, runtime.Core.DatabaseLimits,
                provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
            {
                PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>(),
                PurposeObserver = provider.GetService<IGrainRequestPurposeObserver>()
            };
        });
    }
}
