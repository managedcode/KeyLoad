using System.Collections.Immutable;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class NativeSerializationBenchmarkCorpus
{
    internal const int SmallPayloadBytes = 1024;
    internal const int LargePayloadBytes = 16384;
    internal const string UnicodeText = "Київ🌍";
    private const string JsonPrefix = "{\"text\":\"Київ🌍\",\"escaped\":\"line\\n\\\"quoted\\\"\\\\tail\",\"number\":1.2300,\"padding\":\"";
    private const string JsonSuffix = "\"}";
    private const string PartitionId = "partition-1";
    private static readonly Guid CommandId = new("f76ee337-b9f1-49d7-a407-fb28af7bac98");

    internal static DocumentResult Document(int payloadBytes)
        => new(new(Partition(), "documents", "document-1"), 42, Json(payloadBytes), true, ["secret", UnicodeText]);

    internal static CommandRequest Command(int payloadBytes)
    {
        RequireSize(payloadBytes);
        var dimension = payloadBytes / sizeof(float);
        var values = Enumerable.Range(0, dimension).Select(index => (index % 31 - 15) / 16f).ToImmutableArray();
        var space = new VectorSpace("semantic", dimension, DistanceMetric.Cosine, "model-α", "v1");
        return new(CommandId, Partition(),
        [
            new PutDocument("documents", "document-1", Json(payloadBytes), 41, new("owner", "project"), true),
            new PutDocument("documents", "document-2", "{\"optional\":null}"),
            new PutVector("documents", "document-1", "embedding", values, space, 42)
        ], 7);
    }

    internal static StorageMutation[] Storage(int payloadBytes)
    {
        RequireSize(payloadBytes);
        var value = new byte[payloadBytes];
        for (var index = 0; index < value.Length; index++)
        {
            value[index] = unchecked((byte)(index * 37 + 11));
        }
        return
        [
            new(new byte[] { 0, 255, 1 }, value),
            new(new byte[] { 2, 0, 255 }, ReadOnlyMemory<byte>.Empty),
            new(new byte[] { 3, 255, 0 }, null),
            new(new byte[] { 4, 0, 254 }, Encoding.UTF8.GetBytes(UnicodeText))
        ];
    }

    internal static string Json(int payloadBytes)
    {
        RequireSize(payloadBytes);
        var padding = payloadBytes - Encoding.UTF8.GetByteCount(JsonPrefix) - Encoding.UTF8.GetByteCount(JsonSuffix);
        return JsonPrefix + new string('x', padding) + JsonSuffix;
    }

    private static PartitionRef Partition() => new("tenant", "database", "domain", PartitionId);

    private static void RequireSize(int payloadBytes)
    {
        if (payloadBytes is not (SmallPayloadBytes or LargePayloadBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }
    }
}
