using System.Collections.Immutable;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class NativeSerializationBenchmarkCorpus
{
    private const string CorpusCommandIdentity = "f76ee337-b9f1-49d7-a407-fb28af7bac98";
    private const string CorpusCollection = "documents";
    private const string CorpusDocumentId = "document-1";
    private const int CorpusDocumentRevision = 42;
    private const string RedactedSecretField = "secret";
    private const string CorpusTenant = "tenant";
    private const string CorpusDatabase = "database";
    private const string CorpusDomain = "domain";

    internal const int SmallPayloadBytes = 1024;
    internal const int LargePayloadBytes = 16384;
    internal const string UnicodeText = "Київ🌍";
    private const string JsonPrefix = "{\"text\":\"Київ🌍\",\"escaped\":\"line\\n\\\"quoted\\\"\\\\tail\",\"number\":1.2300,\"padding\":\"";
    private const string JsonSuffix = "\"}";
    private const string PartitionId = "partition-1";
    private static readonly Guid CommandId = new(CorpusCommandIdentity);

    internal static DocumentResult Document(int payloadBytes)
        => new(new(Partition(), CorpusCollection, CorpusDocumentId), CorpusDocumentRevision, Json(payloadBytes), true, [RedactedSecretField, UnicodeText]);

    internal static CommandRequest Command(int payloadBytes)
    {
        const int StartEmptyCount = 0;
        const int VectorCycleLength = 31;
        const int PatternOffset = 15;
        const float PatternScale = 16f;
        const string CommandIdText = "semantic";
        const string CommandModelText = "model-α";
        const string CommandVersionText = "v1";
        const string CommandCollectionText = "documents";
        const string CommandCommandIdText = "document-1";
        const int PreviousDocumentRevision = 41;
        const string CommandOwnerIdText = "owner";
        const string CommandProjectIdText = "project";
        const string CommandJsonText = "{\"optional\":null}";
        const string CommandFieldText = "embedding";
        const int ExpectedVectorDocumentRevision = 42;
        const int CorpusOwnershipEpoch = 7;

        RequireSize(payloadBytes);
        var dimension = payloadBytes / sizeof(float);
        var values = Enumerable.Range(StartEmptyCount, dimension).Select(index => (index % VectorCycleLength - PatternOffset) / PatternScale).ToImmutableArray();
        var space = new VectorSpace(CommandIdText, dimension, DistanceMetric.Cosine, CommandModelText, CommandVersionText);
        return new(CommandId, Partition(),
        [
            new PutDocument(CommandCollectionText, CommandCommandIdText, Json(payloadBytes), PreviousDocumentRevision, new(CommandOwnerIdText, CommandProjectIdText), true),
            new PutDocument(CommandCollectionText, "document-2", CommandJsonText),
            new PutVector(CommandCollectionText, CommandCommandIdText, CommandFieldText, values, space, ExpectedVectorDocumentRevision)
        ], CorpusOwnershipEpoch);
    }

    internal static StorageMutation[] Storage(int payloadBytes)
    {
        const int IndexInitialValue = 0;
        const int PatternScale = 37;
        const int PatternOffset = 11;
        const int StorageEmptyCount = 0;
        const int MaximumCorpusByte = 255;
        const int StorageSingleItemCount = 1;
        const int SecondStorageKeyPrefix = 2;

        RequireSize(payloadBytes);
        var value = new byte[payloadBytes];
        for (var index = IndexInitialValue; index < value.Length; index++)
        {
            value[index] = unchecked((byte)(index * PatternScale + PatternOffset));
        }
        return
        [
            new(new byte[] { StorageEmptyCount, MaximumCorpusByte, StorageSingleItemCount }, value),
            new(new byte[] { SecondStorageKeyPrefix, StorageEmptyCount, MaximumCorpusByte }, ReadOnlyMemory<byte>.Empty),
            new(new byte[] { 3, MaximumCorpusByte, StorageEmptyCount }, null),
            new(new byte[] { 4, StorageEmptyCount, 254 }, Encoding.UTF8.GetBytes(UnicodeText))
        ];
    }

    internal static string Json(int payloadBytes)
    {
        const char CCharacter = 'x';

        RequireSize(payloadBytes);
        var padding = payloadBytes - Encoding.UTF8.GetByteCount(JsonPrefix) - Encoding.UTF8.GetByteCount(JsonSuffix);
        return JsonPrefix + new string(CCharacter, padding) + JsonSuffix;
    }

    private static PartitionRef Partition() => new(CorpusTenant, CorpusDatabase, CorpusDomain, PartitionId);

    private static void RequireSize(int payloadBytes)
    {
        if (payloadBytes is not (SmallPayloadBytes or LargePayloadBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        }
    }
}
