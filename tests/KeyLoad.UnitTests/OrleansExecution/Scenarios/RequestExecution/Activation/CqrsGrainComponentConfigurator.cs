using Orleans.Metadata;

namespace KeyLoad.UnitTests.OrleansExecution;

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

    private static global::ManagedCode.Communication.Result<Func<object>?> CreateFactory(Type grainClass)
    {
        if (grainClass == typeof(CqrsStreamGrain))
        {
            return global::ManagedCode.Communication.Result<Func<object>?>.Succeed(static () => new CqrsStreamGrain());
        }
        if (grainClass == typeof(CqrsLeafGrain))
        {
            return global::ManagedCode.Communication.Result<Func<object>?>.Succeed(static () => new CqrsLeafGrain());
        }
        if (grainClass == typeof(CqrsObservationGrain))
        {
            return global::ManagedCode.Communication.Result<Func<object>?>.Succeed(static () => new CqrsObservationGrain());
        }
        return global::ManagedCode.Communication.Result<Func<object>?>.FailNotFound(nameof(CreateFactory));
    }
}
