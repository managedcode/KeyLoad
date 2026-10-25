using Orleans.Metadata;

namespace KeyLoad.FunctionalTests.OrleansExecution;

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

        var factory = (CreateFactory(grainClass)).Value;
        if (factory is null)
        {
            return;
        }

        shared.SetComponent<IGrainActivator>(new CqrsGrainActivator(
            factory,
            new DefaultGrainActivator(serviceProvider, grainClass)));
    }

    private static (bool HasValue, Func<object>? Value) CreateFactory(Type grainClass)
    {
        if (grainClass == typeof(CqrsStreamGrain))
        {
            return (true, static () => new CqrsStreamGrain());
        }
        if (grainClass == typeof(CqrsLeafGrain))
        {
            return (true, static () => new CqrsLeafGrain());
        }
        if (grainClass == typeof(CqrsObservationGrain))
        {
            return (true, static () => new CqrsObservationGrain());
        }
        return (false, default(Func<object>?));
    }
}
