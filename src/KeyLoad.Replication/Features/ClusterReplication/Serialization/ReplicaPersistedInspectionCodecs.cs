using System.Collections.Immutable;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaHardStateInspectionCodec : ReplicaRecordInspectionCodec<ReplicaHardState>
{
    protected override ReplicaHardState ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, 0),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<string?, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ReplicaSnapshot?, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaHardState value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.Version);
        ReplicaInspectionFields.Write(ref writer, 1, value.Incarnation);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.VotedFor);
        ReplicaInspectionFields.Write(ref writer, 1, value.LastIndex);
        ReplicaInspectionFields.Write(ref writer, 1, value.CommittedIndex);
        ReplicaInspectionFields.Write(ref writer, 1, value.Snapshot);
    }
}

internal sealed class ReplicaMembershipInspectionCodec : ReplicaRecordInspectionCodec<ReplicaBenchmarkMembershipRecord>
{
    protected override ReplicaBenchmarkMembershipRecord ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, 0),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ImmutableArray<string>, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaBenchmarkMembershipRecord value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.Version);
        ReplicaInspectionFields.Write(ref writer, 1, value.Incarnation);
        ReplicaInspectionFields.Write(ref writer, 1, value.VoterIds);
    }
}
