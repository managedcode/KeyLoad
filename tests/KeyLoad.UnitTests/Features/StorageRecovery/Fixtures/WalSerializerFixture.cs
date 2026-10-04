using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class WalSerializerFixture : IDisposable
{
    private const string MissingMutationsMessage = "The test WAL payload did not contain a mutation collection.";
    private readonly ServiceProvider services;
    private readonly Serializer<ZoneTreeJournalMutation[]> serializer;

    internal WalSerializerFixture(bool useNativeByteCodec = true)
    {
        var collection = new ServiceCollection();
        collection.AddSerializer(builder => builder.AddAssembly(typeof(ZoneTreeJournalMutation).Assembly));
        // False reproduces the frame2 provider for legacy refusal fixtures only.
        if (useNativeByteCodec)
        {
            collection.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        }
        services = collection.BuildServiceProvider();
        serializer = services.GetRequiredService<Serializer<ZoneTreeJournalMutation[]>>();
    }

    internal byte[] Serialize(ZoneTreeJournalMutation[]? mutations) => serializer.SerializeToArray(mutations!);

    internal ZoneTreeJournalMutation[] Deserialize(byte[] payload)
        => serializer.Deserialize(payload) ?? throw new InvalidOperationException(MissingMutationsMessage);

    public void Dispose() => services.Dispose();
}
