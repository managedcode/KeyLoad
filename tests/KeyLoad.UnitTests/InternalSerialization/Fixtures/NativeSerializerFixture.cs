using KeyLoad.Features.InternalSerialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Independently invokes the official serializer to author semantic/corruption fixtures.
internal sealed class NativeSerializerFixture : IDisposable
{
    private readonly ServiceProvider services;
    private readonly Serializer<NativePayload> serializer;

    internal NativeSerializerFixture()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        collection.AddSerializer(builder => builder.AddAssembly(typeof(NativeSerialization).Assembly));
        services = collection.BuildServiceProvider();
        serializer = services.GetRequiredService<Serializer<NativePayload>>();
    }

    internal byte[] Encode(object? value, uint version = NativePayloadVersion.Current)
        => serializer.SerializeToArray(new NativePayload { Version = version, Value = value });

    internal byte[] EncodeEnvelope(NativeEnvelopeFault fault) => NativeEnvelopeFixture.Encode(services.GetRequiredService<SerializerSessionPool>(), fault);

    internal byte[] EncodeNullRoot() => serializer.SerializeToArray(null!);

    public void Dispose() => services.Dispose();
}
