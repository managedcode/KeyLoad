using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

// Private admission profile; ordinary Forward writers remain the official generated record codec.
internal sealed class ReplicaForwardRequestInspectionCodec : ReplicaRecordInspectionCodec<ReplicaForwardRequestV1>
{
    protected override ReplicaForwardRequestV1 ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReplicaForwardReadGrantV1?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<DateTimeOffset?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaForwardRequestV1 value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Version);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Operation);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Grant);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.ExpiresAt);
    }
}
