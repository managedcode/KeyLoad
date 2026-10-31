using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

internal sealed class RequestCqrsProbeActivator(DefaultGrainActivator disposalActivator) : IGrainActivator
{
    public object CreateInstance(IGrainContext context)
    {
        var services = context.ActivationServices;
        return new RequestCqrsIdentityProbeGrain(
            services.GetRequiredService<GrainRequestCodec>(),
            services.GetRequiredService<RequestCqrsCapabilityLedger>());
    }

    public ValueTask DisposeInstance(IGrainContext context, object instance)
        => disposalActivator.DisposeInstance(context, instance);
}
