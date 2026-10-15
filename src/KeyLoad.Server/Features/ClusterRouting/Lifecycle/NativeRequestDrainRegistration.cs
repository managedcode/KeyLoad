using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Registers the separate native application-stage request drain without modifying native job ownership.</summary>
internal static class NativeRequestDrainRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
            new NativeRequestDrainLifecycleParticipant(provider.GetRequiredService<Func<NativeRequestWorkOwner>>()()));
}
