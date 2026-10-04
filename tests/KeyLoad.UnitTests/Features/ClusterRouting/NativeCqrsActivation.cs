using Orleans.Metadata;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsGrainComponentConfigurator(
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

        shared.SetComponent<IGrainActivator>(new NativeCqrsGrainActivator(
            factory,
            new DefaultGrainActivator(serviceProvider, grainClass)));
    }

    private static Func<object>? CreateFactory(Type grainClass)
        => grainClass == typeof(NativeCqrsStreamGrain)
            ? static () => new NativeCqrsStreamGrain()
            : grainClass == typeof(NativeCqrsLeafGrain)
                ? static () => new NativeCqrsLeafGrain()
                : grainClass == typeof(NativeCqrsObservationGrain)
                    ? static () => new NativeCqrsObservationGrain()
                    : null;
}

internal sealed class NativeCqrsGrainActivator(
    Func<object> createInstance,
    DefaultGrainActivator disposalActivator) : IGrainActivator
{
    public object CreateInstance(IGrainContext _) => createInstance();

    public ValueTask DisposeInstance(IGrainContext context, object instance)
        => disposalActivator.DisposeInstance(context, instance);
}
