using System.Security.Claims;
using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Metadata;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

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

internal sealed class RequestCqrsProbeActivator(DefaultGrainActivator disposalActivator) : IGrainActivator
{
    public object CreateInstance(IGrainContext context)
    {
        var services = context.ActivationServices;
        return new RequestCqrsIdentityProbeGrain(
            services.GetRequiredService<GrainRequestCodec>(),
            services.GetRequiredService<RequestCqrsCapabilityLedger>(),
            services.GetRequiredService<Serializer<ClaimsPrincipal>>(),
            services.GetRequiredService<Serializer<GrainRequestContextState>>(),
            services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            services.GetRequiredService<IOptions<GrainRoutingOptions>>());
    }

    public ValueTask DisposeInstance(IGrainContext context, object instance)
        => disposalActivator.DisposeInstance(context, instance);
}
