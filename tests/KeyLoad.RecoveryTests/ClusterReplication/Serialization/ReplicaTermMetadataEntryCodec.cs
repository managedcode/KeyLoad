using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.RecoveryTests;

// Test-only native writer keeps the malformed nested operation on the actual entry wire path.
internal sealed class ReplicaTermMetadataEntryCodec(ReplicaMaximumOperationCodec operationCodec) : IFieldCodec<ReplicaEntry>
{
    private const string MissingEntry = "The malformed-entry vector requires a real entry.";
    private const string MissingOperation = "The malformed-entry vector requires a real operation.";

    public ReplicaEntry ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicaEntry value) where TWriter : IBufferWriter<byte>
    {
        if (value is null)
        {
            throw new InvalidOperationException(MissingEntry);
        }
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }

        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaEntry), WireType.TagDelimited);
        writer.WriteEndBase();
        Write(ref writer, 0, value.Index);
        Write(ref writer, 1, value.Term);
        operationCodec.WriteField(ref writer, 1, typeof(ReplicatedOperation),
            value.Operation ?? throw new InvalidOperationException(MissingOperation));
        writer.WriteEndObject();
    }

    private static void Write<T, TWriter>(ref Writer<TWriter> writer, uint delta, T value)
        where TWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}
