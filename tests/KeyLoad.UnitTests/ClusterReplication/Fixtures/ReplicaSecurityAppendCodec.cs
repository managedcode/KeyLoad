using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaSecurityAppendCodec(bool duplicateOperation) : IFieldCodec<AppendRequest>
{
    public AppendRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] AppendRequest value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(AppendRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.LeaderId);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Term);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PreviousIndex);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PreviousTerm);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.CommittedIndex);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Entries);
        if (!duplicateOperation)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 0, value.Entries);
        }
        writer.WriteEndObject();
    }
}
