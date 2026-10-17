using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaSecurityEntryCodec : IFieldCodec<ReplicaEntry>
{
    public ReplicaEntry ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicaEntry value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaEntry), WireType.TagDelimited);
        writer.WriteEndBase();
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Index);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.Term);
        ReplicaSecurityWireFixture.Write<ReplicatedOperation?, TWriter>(ref writer, 1, null);
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Operation);
        writer.WriteEndObject();
    }
}
