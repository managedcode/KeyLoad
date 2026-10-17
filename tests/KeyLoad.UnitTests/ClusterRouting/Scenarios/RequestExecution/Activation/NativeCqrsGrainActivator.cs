namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsGrainActivator(
    Func<object> createInstance,
    DefaultGrainActivator disposalActivator) : IGrainActivator
{
    public object CreateInstance(IGrainContext _) => createInstance();

    public ValueTask DisposeInstance(IGrainContext context, object instance)
        => disposalActivator.DisposeInstance(context, instance);
}
