using System.Collections.Immutable;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaHardStateInspectionCodec : ReplicaRecordInspectionCodec<ReplicaHardState>
{
    protected override ReplicaHardState ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<string?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ReplicaSnapshot?, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaHardState value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Version);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Incarnation);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.VotedFor);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.LastIndex);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.CommittedIndex);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Snapshot);
    }
}

internal sealed class ReplicaMembershipInspectionCodec : ReplicaRecordInspectionCodec<ReplicaBenchmarkMembershipRecord>
{
    protected override ReplicaBenchmarkMembershipRecord ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Read<int, TInput>(ref reader, ReplicaInspectionFields.FirstFieldIdDelta),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<ImmutableArray<string>, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, ReplicaBenchmarkMembershipRecord value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.Version);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Incarnation);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.VoterIds);
    }
}
