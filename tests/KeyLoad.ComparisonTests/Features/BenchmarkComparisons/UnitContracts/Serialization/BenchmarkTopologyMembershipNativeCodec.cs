using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal enum BenchmarkMembershipDefect { Duplicate, Missing, Unknown, WrongVersionScalar }

// Independent native producers deliberately change one authority-field shape.
internal sealed class BenchmarkTopologyMembershipNativeCodec(BenchmarkMembershipDefect defect)
    : IFieldCodec<ReplicaBenchmarkMembershipRecord>
{
    internal static byte[] Encode(ReplicaBenchmarkMembershipRecord value, BenchmarkMembershipDefect defect)
        => ReplicaNativeFixtureEncoder.Encode(value, new BenchmarkTopologyMembershipNativeCodec(defect));

    public ReplicaBenchmarkMembershipRecord ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] ReplicaBenchmarkMembershipRecord value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaBenchmarkMembershipRecord), WireType.TagDelimited);
        writer.WriteEndBase();
        if (defect == BenchmarkMembershipDefect.WrongVersionScalar)
        {
            StringCodec.WriteField(ref writer, 0, BenchmarkTopologyMembershipFixture.PrivateCanary);
        }
        else
        {
            Int32Codec.WriteField(ref writer, 0, value.Version);
        }
        if (defect == BenchmarkMembershipDefect.Duplicate)
        { Int32Codec.WriteField(ref writer, 0, value.Version); }
        writer.Session.CodecProvider.GetCodec<Guid>().WriteField(ref writer, 1, typeof(Guid), value.Incarnation);
        if (defect != BenchmarkMembershipDefect.Missing)
        {
            writer.Session.CodecProvider.GetCodec<ImmutableArray<string>>().WriteField(ref writer, 1, typeof(ImmutableArray<string>), value.VoterIds);
        }
        if (defect == BenchmarkMembershipDefect.Unknown)
        { StringCodec.WriteField(ref writer, 1, BenchmarkTopologyMembershipFixture.PrivateCanary); }
        writer.WriteEndObject();
    }
}
