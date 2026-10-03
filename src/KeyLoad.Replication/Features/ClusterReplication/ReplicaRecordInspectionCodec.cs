using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

internal abstract class ReplicaRecordInspectionCodec<T> : IFieldCodec<T> where T : class
{
    [return: MaybeNull]
    public T ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.FieldType is not null && field.FieldType != typeof(T))
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        if (field.IsReference)
        {
            return ReplicaInspectionFields.Reference<T, TInput>(ref reader);
        }
        field.EnsureWireTypeTagDelimited();
        var reference = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        // Explicit property IDs belong to the body after the generated empty constructor scope.
        ReplicaInspectionFields.EndBase(ref reader);
        var result = ReadFields(ref reader);
        ReplicaInspectionFields.End(ref reader);
        ReferenceCodec.RecordObject(reader.Session, result, reference);
        return result;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] T value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(T), WireType.TagDelimited);
        writer.WriteEndBase();
        WriteFields(ref writer, value);
        writer.WriteEndObject();
    }

    protected abstract T ReadFields<TInput>(ref Reader<TInput> reader);
    protected abstract void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, T value) where TBufferWriter : IBufferWriter<byte>;
}
