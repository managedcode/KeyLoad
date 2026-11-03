using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;

namespace KeyLoad.FunctionalTests.InternalSerialization;

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

    internal byte[] Encode(object? value)
        => serializer.SerializeToArray(new RuntimePayload { Value = value });

    internal byte[] EncodeEnvelope(EnvelopeFault fault) => fault switch
    {
        EnvelopeFault.WrongRoot => Encode(new QueueCounters(1, 1, 1, 1, 1)),
        EnvelopeFault.MissingValue => Encode(null),
        _ => throw new ArgumentOutOfRangeException(nameof(fault))
    };

    internal byte[] EncodeNullRoot() => serializer.SerializeToArray(null!);

    public void Dispose() => services.Dispose();
}
