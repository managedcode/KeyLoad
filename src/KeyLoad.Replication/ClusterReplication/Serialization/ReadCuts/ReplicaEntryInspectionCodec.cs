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
