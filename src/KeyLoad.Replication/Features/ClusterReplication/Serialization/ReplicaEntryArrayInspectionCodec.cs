using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

internal abstract class ReplicaArrayInspectionCodec<T>(IFieldCodec<T> elementCodec) : IFieldCodec<T[]> where T : class
{
    private const int EmptyArrayLength = 0;
    private const int FirstElementIndex = 0;
    private const uint FirstElementFieldIdDelta = 1U;
    private const uint RepeatedElementFieldIdDelta = 0U;
    private readonly ArrayCodec<T> writerCodec = new(elementCodec);

    [return: MaybeNull]
    public T[] ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        if (field.IsReference)
        {
            return ReplicaInspectionFields.Reference<T[], TInput>(ref reader);
        }
        field.EnsureWireTypeTagDelimited();
        var reference = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
        var countField = reader.ReadFieldHeader();
        if (countField.IsEndObject)
        {
            var empty = Array.Empty<T>();
            ReferenceCodec.RecordObject(reader.Session, empty, reference);
            return empty;
        }
        ReplicaInspectionFields.Require(countField, ReplicaInspectionFields.FirstFieldIdDelta, typeof(uint));
        var count = UInt32Codec.ReadValue(ref reader, countField);
        var maximum = ReplicaInspectionBuffers.For(reader.Session).MaximumEntries;
        if (count == EmptyArrayLength || count > maximum)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        reader.EnsureAvailable(count);
        var result = new T[checked((int)count)];
        ReferenceCodec.RecordObject(reader.Session, result, reference);
        for (var index = FirstElementIndex; index < result.Length; index++)
        {
            var item = reader.ReadFieldHeader();
            ReplicaInspectionFields.Require(item, index == FirstElementIndex ? FirstElementFieldIdDelta : RepeatedElementFieldIdDelta, typeof(T));
            result[index] = elementCodec.ReadValue(ref reader, item)
                ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        ReplicaInspectionFields.End(ref reader);
        return result;
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] T[] value) where TBufferWriter : IBufferWriter<byte>
        => writerCodec.WriteField(ref writer, fieldIdDelta, expectedType, value);
}

internal sealed class ReplicaEntryArrayInspectionCodec(IFieldCodec<ReplicaEntry> elementCodec)
    : ReplicaArrayInspectionCodec<ReplicaEntry>(elementCodec);

internal sealed class ReplicaVoterArrayInspectionCodec(IFieldCodec<string> elementCodec)
    : ReplicaArrayInspectionCodec<string>(elementCodec);
