using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace KeyLoad.Features.InternalSerialization;

internal sealed class NativeSerializerContext(ServiceProvider services)
{
    internal SerializerSessionPool Sessions { get; } = services.GetRequiredService<SerializerSessionPool>();

    internal Serializer<NativePayload> Serializer { get; } = services.GetRequiredService<Serializer<NativePayload>>();
}
