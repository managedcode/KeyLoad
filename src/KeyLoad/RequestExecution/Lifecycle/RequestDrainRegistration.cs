using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Registers the separate execution application-stage request drain without modifying execution job ownership.</summary>
internal static class RequestDrainRegistration
{
    internal static void Register(IServiceCollection services)
        => services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
            new RequestDrainLifecycleParticipant(provider.GetRequiredService<Func<RequestWorkOwner>>()()));
}
