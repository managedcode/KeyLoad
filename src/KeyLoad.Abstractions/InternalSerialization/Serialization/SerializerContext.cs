using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class SerializerContext(ServiceProvider services)
{
    internal SerializerSessionPool Sessions { get; } = services.GetRequiredService<SerializerSessionPool>();

    internal Serializer<RuntimePayload> Serializer { get; } = services.GetRequiredService<Serializer<RuntimePayload>>();
}
