using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

// Fixed scalar statistics retain the original private inspection session and strict record framing.
internal sealed class ReplicaForwardStatsInspectionCodec : ReplicaRecordInspectionCodec<ReplicaForwardReadStatsV1>
{
    protected override ReplicaForwardReadStatsV1 ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaForwardReadStatsV1 value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.ReadBytes);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.ExaminedRecords);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.ResultBytes);
    }
}
