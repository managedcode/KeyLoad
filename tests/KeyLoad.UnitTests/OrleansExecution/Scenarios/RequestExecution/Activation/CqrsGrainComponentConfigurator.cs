using Orleans.Metadata;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CqrsGrainComponentConfigurator(
    GrainClassMap grainClassMap,
    IServiceProvider serviceProvider) : IConfigureGrainTypeComponents
{
    public void Configure(GrainType grainType, GrainProperties _, GrainTypeSharedContext shared)
    {
        if (!grainClassMap.TryGetGrainClass(grainType, out var grainClass))
        {
            return;
        }

        var factory = CreateFactory(grainClass);
        if (factory is null)
        {
            return;
        }

        shared.SetComponent<IGrainActivator>(new CqrsGrainActivator(
            factory,
            new DefaultGrainActivator(serviceProvider, grainClass)));
    }

    private static Func<object>? CreateFactory(Type grainClass)
        => grainClass == typeof(CqrsStreamGrain)
            ? static () => new CqrsStreamGrain()
            : grainClass == typeof(CqrsLeafGrain)
                ? static () => new CqrsLeafGrain()
                : grainClass == typeof(CqrsObservationGrain)
                    ? static () => new CqrsObservationGrain()
                    : null;
}
