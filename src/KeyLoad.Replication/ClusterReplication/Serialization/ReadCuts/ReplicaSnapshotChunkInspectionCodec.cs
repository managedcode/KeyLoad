using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotChunkInspectionCodec : ReplicaRecordInspectionCodec<SnapshotChunkRequest>
{
    protected override SnapshotChunkRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotChunkRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.TransferId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Offset);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Bytes);
    }
}
