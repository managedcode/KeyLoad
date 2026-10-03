using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaEntryInspectionCodec : ReplicaRecordInspectionCodec<ReplicaEntry>
{
    protected override ReplicaEntry ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 0),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ReplicatedOperation?, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaEntry value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.Index);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.Operation);
    }
}

// Generated record constructor scope is handled by the shared record inspection codec.
internal sealed class ReplicaOperationInspectionCodec : ReplicaRecordInspectionCodec<ReplicatedOperation>
{
    protected override ReplicatedOperation ReadFields<TInput>(ref Reader<TInput> reader)
    {
        var result = new ReplicatedOperation(
            ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 0),
            ReplicaInspectionFields.Read<OperationKind, TInput>(ref reader, 1),
            ReplicaInspectionFields.Read<string, TInput>(ref reader, 1),
            ReplicaInspectionFields.Read<DateTimeOffset, TInput>(ref reader, 1),
            ReplicaInspectionFields.Read<string, TInput>(ref reader, 1));
        return result with { NativePayload = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, 1) };
    }

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicatedOperation value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.Id);
        ReplicaInspectionFields.Write(ref writer, 1, value.Kind);
        ReplicaInspectionFields.Write(ref writer, 1, value.PrincipalId);
        ReplicaInspectionFields.Write(ref writer, 1, value.EvaluatedAt);
        ReplicaInspectionFields.Write(ref writer, 1, value.PayloadJson);
        ReplicaInspectionFields.Write(ref writer, 1, value.NativePayload);
    }
}
