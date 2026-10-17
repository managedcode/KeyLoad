using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

internal sealed class ReplicaSecurityOperationCodec(string shape) : IFieldCodec<ReplicatedOperation>
{
    public ReplicatedOperation ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TWriter>(ref Writer<TWriter> writer, uint delta, [AllowNull] Type expectedType,
        [AllowNull] ReplicatedOperation value) where TWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        {
            return;
        }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicatedOperation), WireType.TagDelimited);
        writer.WriteEndBase();
        if (shape == ReplicaSecurityFixture.MissingOperationFields)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 1, value.Kind);
            writer.WriteEndObject();
            return;
        }
        ReplicaSecurityWireFixture.Write(ref writer, 0, value.Id);
        ReplicaSecurityWireFixture.Write(ref writer, 1, shape == ReplicaSecurityFixture.UnknownKind ? (OperationKind)int.MaxValue : value.Kind);
        if (shape == ReplicaSecurityFixture.DuplicateKind)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 0, value.Kind);
        }
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.PrincipalId);
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.EvaluatedAt);
        if (shape == ReplicaSecurityFixture.InvalidPayload)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 1, false);
        }
        else
        {
            ReplicaSecurityWireFixture.Write(ref writer, 1, value.PayloadJson);
        }
        ReplicaSecurityWireFixture.Write(ref writer, 1, value.NativePayload);
        if (shape == ReplicaSecurityFixture.UnknownField)
        {
            ReplicaSecurityWireFixture.Write(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}
