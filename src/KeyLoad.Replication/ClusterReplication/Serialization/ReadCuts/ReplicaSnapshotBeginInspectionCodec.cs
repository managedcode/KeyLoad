using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotBeginInspectionCodec : ReplicaRecordInspectionCodec<SnapshotBeginRequest>
{
    protected override SnapshotBeginRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReplicaSnapshot, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotBeginRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Snapshot);
    }
}
