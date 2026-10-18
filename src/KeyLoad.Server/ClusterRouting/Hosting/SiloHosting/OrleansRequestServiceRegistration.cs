using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider => new GrainRequestCodec(provider.GetRequiredService<DatabaseEngine>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
        {
            PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>(),
            PurposeObserver = provider.GetService<IGrainRequestPurposeObserver>()
        });
    }
}
