using System.Collections.Immutable;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

internal sealed class ReplicaVoteInspectionCodec : ReplicaRecordInspectionCodec<VoteRequest>
{
    protected override VoteRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, VoteRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.CandidateId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.LastIndex);
        ReplicaInspectionFields.Write(ref writer, 1, value.LastTerm);
    }
}

internal sealed class ReplicaAppendInspectionCodec : ReplicaRecordInspectionCodec<AppendRequest>
{
    protected override AppendRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ImmutableArray<ReplicaEntry>, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, AppendRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.PreviousIndex);
        ReplicaInspectionFields.Write(ref writer, 1, value.PreviousTerm);
        ReplicaInspectionFields.Write(ref writer, 1, value.CommittedIndex);
        ReplicaInspectionFields.Write(ref writer, 1, value.Entries);
    }
}

internal sealed class ReplicaSnapshotBeginInspectionCodec : ReplicaRecordInspectionCodec<SnapshotBeginRequest>
{
    protected override SnapshotBeginRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ReplicaSnapshot, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotBeginRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.Snapshot);
    }
}

internal sealed class ReplicaSnapshotChunkInspectionCodec : ReplicaRecordInspectionCodec<SnapshotChunkRequest>
{
    protected override SnapshotChunkRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotChunkRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.TransferId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Offset);
        ReplicaInspectionFields.Write(ref writer, 1, value.Bytes);
    }
}

internal sealed class ReplicaSnapshotCompleteInspectionCodec : ReplicaRecordInspectionCodec<SnapshotCompleteRequest>
{
    protected override SnapshotCompleteRequest ReadFields<TInput>(ref Reader<TInput> reader) => new(
        ReplicaInspectionFields.Sender(ref reader),
        ReplicaInspectionFields.Read<long, TInput>(ref reader, 1),
        ReplicaInspectionFields.Read<Guid, TInput>(ref reader, 1));

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, SnapshotCompleteRequest value)
    {
        ReplicaInspectionFields.Write(ref writer, 0, value.LeaderId);
        ReplicaInspectionFields.Write(ref writer, 1, value.Term);
        ReplicaInspectionFields.Write(ref writer, 1, value.TransferId);
    }
}
