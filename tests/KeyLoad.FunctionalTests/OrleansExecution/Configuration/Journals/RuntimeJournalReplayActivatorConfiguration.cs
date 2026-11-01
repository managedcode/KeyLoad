using Orleans.Metadata;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Constructs the replay grain inside its real execution activation context.</summary>
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
            shared.SetComponent<IGrainActivator>(new CqrsGrainActivator(
                static () => new RuntimeJournalReplayGrain(),
                new DefaultGrainActivator(services, grainClass)));
        }
    }
}
