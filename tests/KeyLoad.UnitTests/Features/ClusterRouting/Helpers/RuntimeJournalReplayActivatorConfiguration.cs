using Orleans.Metadata;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Constructs the replay grain inside its real native activation context.</summary>
internal sealed class RuntimeJournalReplayActivatorConfiguration(
    GrainClassMap grainClasses, IServiceProvider services) : IConfigureGrainTypeComponents
{
    public void Configure(GrainType grainType, GrainProperties _, GrainTypeSharedContext shared)
    {
        if (grainClasses.TryGetGrainClass(grainType, out var grainClass)
            && grainClass == typeof(RuntimeJournalReplayGrain))
        {
            shared.SetComponent<IGrainActivator>(new NativeCqrsGrainActivator(
                static () => new RuntimeJournalReplayGrain(),
                new DefaultGrainActivator(services, grainClass)));
        }
    }
}
