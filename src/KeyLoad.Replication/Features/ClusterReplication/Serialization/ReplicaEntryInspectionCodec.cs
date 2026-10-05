using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaEntryInspectionCodec : ReplicaRecordInspectionCodec<ReplicaEntry>
{
    protected override ReplicaEntry ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReplicatedOperation?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaEntry value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Index);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Operation);
    }
}

// Generated record constructor scope is handled by the shared record inspection codec.
internal sealed class ReplicaOperationInspectionCodec : ReplicaRecordInspectionCodec<ReplicatedOperation>
{
    protected override ReplicatedOperation ReadFields<TInput>(ref Reader<TInput> reader)
    {
        var result = new ReplicatedOperation(
            ReplicaInspectionFields.Read<Guid, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
            ReplicaInspectionFields.Read<OperationKind, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
            ReplicaInspectionFields.Read<string, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
            ReplicaInspectionFields.Read<DateTimeOffset, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
            ReplicaInspectionFields.Read<string, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));
        return result with { NativePayload = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta) };
    }

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicatedOperation value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Id);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Kind);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.PrincipalId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.EvaluatedAt);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.PayloadJson);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.NativePayload);
    }
}
