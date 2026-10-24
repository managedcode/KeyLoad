using Orleans.Metadata;

namespace KeyLoad.RecoveryTests.OrleansExecution;

internal sealed class RequestCqrsGrainComponentConfigurator(
    GrainClassMap grainClassMap, IServiceProvider serviceProvider) : IConfigureGrainTypeComponents
{
    public void Configure(GrainType grainType, GrainProperties _, GrainTypeSharedContext shared)
    {
        if (grainClassMap.TryGetGrainClass(grainType, out var grainClass)
            && grainClass == typeof(RequestCqrsIdentityProbeGrain))
        {
            shared.SetComponent<IGrainActivator>(new RequestCqrsProbeActivator(
                new DefaultGrainActivator(serviceProvider, grainClass)));
        }
    }
}
