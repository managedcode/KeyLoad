using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaMalformedHardStateCodec(ReplicaMalformedVoteShape shape) : IFieldCodec<ReplicaHardState>
{
    public ReplicaHardState ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] ReplicaHardState value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(ReplicaHardState), WireType.TagDelimited);
        writer.WriteEndBase();
        writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, 0, typeof(int), value.Version);
        if (shape == ReplicaMalformedVoteShape.Duplicate)
        {
            writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, 0, typeof(int), value.Version);
        }
        writer.Session.CodecProvider.GetCodec<Guid>().WriteField(ref writer, 1, typeof(Guid), value.Incarnation);
        if (shape == ReplicaMalformedVoteShape.WrongType)
        {
            StringCodec.WriteField(ref writer, 1, value.VotedFor);
        }
        else if (shape != ReplicaMalformedVoteShape.Missing)
        {
            Int64Codec.WriteField(ref writer, 1, value.Term);
        }
        StringCodec.WriteField(ref writer, shape == ReplicaMalformedVoteShape.Missing ? 2U : 1U, value.VotedFor);
        Int64Codec.WriteField(ref writer, 1, value.LastIndex);
        Int64Codec.WriteField(ref writer, 1, value.CommittedIndex);
        writer.Session.CodecProvider.GetCodec<ReplicaSnapshot>().WriteField(ref writer, 1, typeof(ReplicaSnapshot), value.Snapshot!);
        if (shape == ReplicaMalformedVoteShape.Unknown)
        {
            BoolCodec.WriteField(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}
