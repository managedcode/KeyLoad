using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaVoteInspectionCodec : ReplicaRecordInspectionCodec<VoteRequest>
{
    protected override VoteRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, ReplicaInspectionFields.FollowingFieldIdDelta));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, VoteRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FirstFieldIdDelta, value.CandidateId);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.Term);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.LastIndex);
        ReplicaInspectionFields.Write(ref writer, ReplicaInspectionFields.FollowingFieldIdDelta, value.LastTerm);
    }
}
