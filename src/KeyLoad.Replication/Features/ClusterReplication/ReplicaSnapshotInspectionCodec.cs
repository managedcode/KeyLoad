using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotInspectionCodec : ReplicaRecordInspectionCodec<ReplicaSnapshot>
{
    protected override ReplicaSnapshot ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 0),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<string, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<string, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaSnapshot value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.TransferId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Incarnation);
        ReplicaInspectionFields.Write(ref writer, 1, value.Index);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.Length);
        ReplicaInspectionFields.Write(ref writer, 1, value.Sha256);
        ReplicaInspectionFields.Write(ref writer, 1, value.FileName);
    }
}
