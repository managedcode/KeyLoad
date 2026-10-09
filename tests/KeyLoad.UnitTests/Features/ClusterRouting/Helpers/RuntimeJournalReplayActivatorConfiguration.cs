using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Metadata;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs the replay grain inside its real native activation context.</summary>
internal sealed class RuntimeJournalReplayActivatorConfiguration(
    GrainClassMap grainClasses, IServiceProvider services) : IConfigureGrainTypeComponents
{
    public void Configure(GrainType grainType, GrainProperties _, GrainTypeSharedContext shared)
    {
        if (!grainClasses.TryGetGrainClass(grainType, out var grainClass))
        {
            return;
        }
        if (grainClass == typeof(RuntimeJournalReplayGrain))
        {
            shared.SetComponent<IGrainActivator>(new NativeCqrsGrainActivator(
                static () => new RuntimeJournalReplayGrain(),
                new DefaultGrainActivator(services, grainClass)));
        }
        else if (grainClass == typeof(RuntimeJournalGraphCallerProbeGrain))
        {
            shared.SetComponent<IGrainActivator>(new NativeCqrsGrainActivator(
                () => new RuntimeJournalGraphCallerProbeGrain(
                    services.GetRequiredService<IGrainFactory>(),
                    services.GetRequiredService<GrainRequestCodec>(),
                    services.GetRequiredService<RuntimeJournalClient>(),
                    services.GetRequiredService<IOptions<NativeRuntimeTestOptions>>(),
                    services.GetRequiredService<NativeConnectionOwnerIdentity>()),
                new DefaultGrainActivator(services, grainClass)));
        }
    }
}
