using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaMalformedVoteCodec(ReplicaMalformedVoteShape shape) : IFieldCodec<VoteRequest>
{
    public VoteRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] VoteRequest value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(VoteRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        StringCodec.WriteField(ref writer, 0, value.CandidateId);
        if (shape == ReplicaMalformedVoteShape.Duplicate)
        {
            StringCodec.WriteField(ref writer, 0, value.CandidateId);
        }
        if (shape == ReplicaMalformedVoteShape.WrongType)
        {
            StringCodec.WriteField(ref writer, 1, value.CandidateId);
        }
        else
        {
            Int64Codec.WriteField(ref writer, 1, value.Term);
        }
        Int64Codec.WriteField(ref writer, 1, value.LastIndex);
        if (shape != ReplicaMalformedVoteShape.Missing)
        {
            Int64Codec.WriteField(ref writer, 1, value.LastTerm);
        }
        if (shape == ReplicaMalformedVoteShape.Unknown)
        {
            BoolCodec.WriteField(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}
