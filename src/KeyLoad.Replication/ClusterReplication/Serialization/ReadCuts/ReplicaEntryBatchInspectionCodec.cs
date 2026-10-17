using System.Collections.Immutable;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaEntryBatchInspectionCodec : ReplicaRecordInspectionCodec<ReplicaEntryBatch>
{
    protected override ReplicaEntryBatch ReadFields<TInput>(ref Reader<TInput> reader)
        => new(ReplicaInspectionFields.Read<ImmutableArray<ReplicaEntry>, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaEntryBatch value)
        => ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Entries);
}
