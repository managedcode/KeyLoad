using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

internal abstract class ReplicaImmutableArrayInspectionCodec<T>(IValueSerializer<ImmutableArraySurrogate<T>> surrogate)
    : IFieldCodec<ImmutableArray<T>> where T : class
{
    private readonly ImmutableArrayCodec<T> writerCodec = new(surrogate);

    public ImmutableArray<T> ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        field.EnsureWireTypeTagDelimited();
        ReferenceCodec.MarkValueField(reader.Session);
        var values = ReplicaInspectionFields.Read<T[], TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta)
            ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        ReplicaInspectionFields.End(ref reader);
        return ImmutableArray.Create<T>(values);
    }

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, ImmutableArray<T> value) where TBufferWriter : IBufferWriter<byte>
        => writerCodec.WriteField(ref writer, fieldIdDelta, expectedType, value);
}

internal sealed class ReplicaEntriesInspectionCodec(IValueSerializer<ImmutableArraySurrogate<ReplicaEntry>> surrogate)
    : ReplicaImmutableArrayInspectionCodec<ReplicaEntry>(surrogate);

internal sealed class ReplicaVotersInspectionCodec(IValueSerializer<ImmutableArraySurrogate<string>> surrogate)
    : ReplicaImmutableArrayInspectionCodec<string>(surrogate);

internal sealed class ReplicaEntryBatchInspectionCodec : ReplicaRecordInspectionCodec<ReplicaEntryBatch>
{
    protected override ReplicaEntryBatch ReadFields<TInput>(ref Reader<TInput> reader)
        => new(ReplicaInspectionFields.Read<ImmutableArray<ReplicaEntry>, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaEntryBatch value)
        => ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Entries);
}
