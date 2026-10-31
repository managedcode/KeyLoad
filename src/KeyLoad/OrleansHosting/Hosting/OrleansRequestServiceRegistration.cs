using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<ServerRuntimeOptions>();
            var node = runtime.Node.Value;
            return new GrainRequestCodec(node.Incarnation,
                provider.GetRequiredService<TimeProvider>())
            {
                PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>(),
                PurposeObserver = provider.GetService<IGrainRequestPurposeObserver>()
            };
        });
    }
}
