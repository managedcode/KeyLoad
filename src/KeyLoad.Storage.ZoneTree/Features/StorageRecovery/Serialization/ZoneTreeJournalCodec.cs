using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeJournalCodec
{
    internal const string JournalPayloadInvalid = "The redo journal mutation payload is invalid.";
    internal const string JournalRecordsInvalid = "The redo journal keys are not strictly ordered unique records.";

    // The provider owns the serializer-only services for the process lifetime, independently
    // of silo activations and individual writes. Sessions are returned to the native pool.
    private static readonly ServiceProvider Services = CreateServices();
    private static readonly Serializer<ZoneTreeJournalMutation[]> Serializer = Services.GetRequiredService<Serializer<ZoneTreeJournalMutation[]>>();
    private static readonly SerializerSessionPool Sessions = Services.GetRequiredService<SerializerSessionPool>();

    internal static byte[] Serialize(StorageMutation[] mutations, int maxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
        var records = CreateRecords(mutations);
        using var buffer = new ZoneTreeJournalBufferWriter(maxFrameBytes);
        using var session = Sessions.GetSession();
        var writer = Writer.Create(buffer, session);
        Serializer.Serialize(records, ref writer);
        return buffer.ToArray();
    }

    internal static StorageMutation[] Deserialize(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalPayloadInvalid);
        }

        ZoneTreeJournalMutation[]? records;
        try
        {
            using var session = Sessions.GetSession();
            var reader = Reader.Create(payload.AsSpan(), session);
            records = Serializer.Deserialize(ref reader);
            if (records is null || records.Length == 0 || reader.Remaining != 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, JournalPayloadInvalid);
            }
        }
        catch (Exception exception) when (IsMalformedPayload(exception))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalPayloadInvalid);
        }

        return CreateMutations(records);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        services.AddSerializer(builder => builder.AddAssembly(typeof(ZoneTreeJournalMutation).Assembly));
        return services.BuildServiceProvider();
    }

    private static ZoneTreeJournalMutation[] CreateRecords(StorageMutation[] mutations)
    {
        var records = new ZoneTreeJournalMutation[mutations.Length];
        ReadOnlyMemory<byte>? previousKey = null;
        for (var index = 0; index < mutations.Length; index++)
        {
            var mutation = mutations[index] ?? throw Errors.Fail(ErrorCode.Corruption, JournalRecordsInvalid);
            ValidateKey(mutation.Key, previousKey);
            records[index] = new ZoneTreeJournalMutation
            {
                Key = mutation.Key,
                Value = mutation.Value,
                Kind = mutation.Value.HasValue ? ZoneTreeJournalMutation.PutKind : ZoneTreeJournalMutation.DeleteKind
            };
            previousKey = mutation.Key;
        }

        return records;
    }

    private static StorageMutation[] CreateMutations(ZoneTreeJournalMutation[] records)
    {
        var mutations = new StorageMutation[records.Length];
        ReadOnlyMemory<byte>? previousKey = null;
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            ValidateKind(record);
            ValidateKey(record.Key, previousKey);
            mutations[index] = new StorageMutation(record.Key, record.Value);
            previousKey = record.Key;
        }

        return mutations;
    }

    private static void ValidateKind(ZoneTreeJournalMutation record)
    {
        if (record.Kind != ZoneTreeJournalMutation.PutKind && record.Kind != ZoneTreeJournalMutation.DeleteKind
            || record.Value.HasValue != (record.Kind == ZoneTreeJournalMutation.PutKind))
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalRecordsInvalid);
        }
    }

    private static void ValidateKey(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte>? previousKey)
    {
        if (key.IsEmpty || previousKey is { } previous && previous.Span.SequenceCompareTo(key.Span) >= 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, JournalRecordsInvalid);
        }
    }

    internal static bool IsMalformedPayload(Exception exception)
        => exception is SerializerException or ArgumentException or InvalidOperationException
            or IndexOutOfRangeException or OverflowException or InvalidCastException
            or FormatException or NotSupportedException or EndOfStreamException or TypeLoadException;
}
