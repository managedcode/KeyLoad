using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

// Private inspection lends result bytes until the source validates the authenticated original grant.
internal sealed class ReplicaForwardReplyInspectionCodec : ReplicaRecordInspectionCodec<ReplicaForwardReplyV1>
{
    protected override ReplicaForwardReplyV1 ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ErrorCode?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<string?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReplicaForwardReadStatsV1?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaForwardReplyV1 value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Version);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Result);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Error);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.SafeDetail);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Statistics);
    }
}
