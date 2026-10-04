using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

// Every inline string receives a unique small proxy; native references preserve its identity.
// Only the isolated native counter writer emits its original borrowed UTF8.
internal sealed class ReplicaBorrowedStringCodec : IFieldCodec<string>
{
    [return: MaybeNull]
    public string ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            var referencedProxy = ReplicaInspectionFields.Reference<string, TInput>(ref reader);
            if (referencedProxy is null)
            {
                return null;
            }
            _ = ReplicaInspectionBuffers.For(reader.Session).Utf8(referencedProxy);
            return referencedProxy;
        }
        field.EnsureWireType(WireType.LengthPrefixed);
        var length = reader.ReadVarUInt32();
        reader.EnsureAvailable(length);
        var scope = ReplicaInspectionBuffers.For(reader.Session);
        var bytes = scope.Borrow(reader.Position, length);
        reader.Skip(length);
        var proxy = scope.String(bytes);
        ReferenceCodec.RecordObject(reader.Session, proxy);
        return proxy;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] string value) where TBufferWriter : IBufferWriter<byte>
    {
        var scope = ReplicaInspectionBuffers.For(writer.Session);
        if (!scope.Counting)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }
        var bytes = scope.Utf8(value);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(string), WireType.LengthPrefixed);
        writer.WriteVarUInt32(checked((uint)bytes.Length));
        writer.Write(bytes.Span);
    }
}
