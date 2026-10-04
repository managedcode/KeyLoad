using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

// Official ROM<byte> writer is retained. Inspection lends inline bytes and records a small
// reference marker instead of allocating the official reader's byte[] before admission.
internal sealed class ReplicaBorrowedBytesCodec : IFieldCodec<ReadOnlyMemory<byte>>
{
    private readonly IFieldCodec<ReadOnlyMemory<byte>> writerCodec = new ReadOnlyMemoryOfByteCodec();

    public ReadOnlyMemory<byte> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            var reference = reader.ReadVarUInt32();
            if (reference == 0)
            {
                return default;
            }
            return reader.Session.ReferencedObjects.TryGetReferencedObject(reference) is ReplicaBorrowedBytes bytes
                ? bytes.Bytes : throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        field.EnsureWireType(WireType.LengthPrefixed);
        var length = reader.ReadVarUInt32();
        reader.EnsureAvailable(length);
        var result = ReplicaInspectionBuffers.For(reader.Session).Borrow(reader.Position, length);
        reader.Skip(length);
        ReferenceCodec.RecordObject(reader.Session, new ReplicaBorrowedBytes(result));
        return result;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, ReadOnlyMemory<byte> value) where TBufferWriter : IBufferWriter<byte>
        => writerCodec.WriteField(ref writer, fieldIdDelta, expectedType, value);
}
