using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotCompleteInspectionCodec : ReplicaRecordInspectionCodec<SnapshotCompleteRequest>
{
    protected override SnapshotCompleteRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotCompleteRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.TransferId);
    }
}
