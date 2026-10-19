using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Independently invokes the official serializer to author semantic/corruption fixtures.
internal sealed class SerializerFixture : IDisposable
{
    private readonly ServiceProvider services;
    private readonly Serializer<RuntimePayload> serializer;

    internal SerializerFixture()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        collection.AddSerializer(builder => builder.AddAssembly(typeof(OrleansSerialization).Assembly));
        services = collection.BuildServiceProvider();
        serializer = services.GetRequiredService<Serializer<RuntimePayload>>();
    }

    internal byte[] Encode(object? value, uint version = PayloadVersion.Current)
        => serializer.SerializeToArray(new RuntimePayload { Version = version, Value = value });

    internal byte[] EncodeEnvelope(EnvelopeFault fault) => EnvelopeFixture.Encode(services.GetRequiredService<SerializerSessionPool>(), fault);

    internal byte[] EncodeNullRoot() => serializer.SerializeToArray(null!);

    public void Dispose() => services.Dispose();
}
