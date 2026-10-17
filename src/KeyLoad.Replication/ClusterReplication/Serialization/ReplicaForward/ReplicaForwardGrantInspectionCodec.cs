using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

// Numeric nested record profile shares the original inspection session and reference table.
internal sealed class ReplicaForwardGrantInspectionCodec : ReplicaRecordInspectionCodec<ReplicaForwardReadGrantV1>
{
    protected override ReplicaForwardReadGrantV1 ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaForwardReadGrantV1 value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.MaximumReadBytes);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.MaximumExaminedRecords);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.MaximumResultBytes);
    }
}
