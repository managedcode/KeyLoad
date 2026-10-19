using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// The same official writer authors complete valid controls and unknown type metadata.
internal static class UnknownWellKnownHeaderFixture
{
    internal const string Alias = "keyload.tests.unknown-well-known-header";
    internal const string Canary = "private-unknown-header-canary";
    internal const uint UnknownTypeId = uint.MaxValue;
    internal const int Number = 42;
    private const string RegisteredAdversarialId = "The adversarial type ID must be unregistered.";

    internal static byte[] Encode(UnknownWellKnownHeaderScope scope, uint delta, bool malformed)
    {
        using var session = SerializerProviders.Get(typeof(UnknownWellKnownHeaderRecord)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            ReferenceCodec.MarkValueField(session);
            Header(ref writer, 0, typeof(RuntimePayload), typeof(RuntimePayload),
                scope == UnknownWellKnownHeaderScope.Envelope && malformed);
            UInt32Codec.WriteField(ref writer, 0, PayloadVersion.Current);
            ReferenceCodec.MarkValueField(session);
            Header(ref writer, 1, typeof(object), typeof(UnknownWellKnownHeaderRecord),
                scope == UnknownWellKnownHeaderScope.Value && malformed);
            writer.WriteEndBase();
            StringCodec.WriteField(ref writer, 0, Canary);
            Scalar(ref writer, 1, scope == UnknownWellKnownHeaderScope.Member && malformed);
            if (scope == UnknownWellKnownHeaderScope.UnknownMember)
            {
                Scalar(ref writer, delta, malformed);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    internal static void Unknown<TWriter>(ref Writer<TWriter> writer, uint delta, WireType wire)
        where TWriter : IBufferWriter<byte>
    {
        if (writer.Session.WellKnownTypes.TryGetWellKnownType(UnknownTypeId, out _))
        {
            throw new InvalidOperationException(RegisteredAdversarialId);
        }
        WellKnown(ref writer, delta, wire, UnknownTypeId);
    }

    internal static void WellKnown<TWriter>(ref Writer<TWriter> writer, uint delta, WireType wire, uint typeId)
        where TWriter : IBufferWriter<byte>
    {
        var tag = new Tag(0)
        {
            WireType = wire,
            SchemaType = SchemaType.WellKnown,
            FieldIdDelta = delta > Tag.MaxEmbeddedFieldIdDelta ? Tag.FieldIdCompleteMask : delta
        };
        writer.WriteByte(tag);
        if (delta > Tag.MaxEmbeddedFieldIdDelta)
        {
            writer.WriteVarUInt32(delta);
        }
        writer.WriteVarUInt32(typeId);
    }

    private static void Header<TWriter>(ref Writer<TWriter> writer, uint delta, Type expected, Type actual, bool malformed)
        where TWriter : IBufferWriter<byte>
    {
        if (malformed)
        {
            Unknown(ref writer, delta, WireType.TagDelimited);
        }
        else
        {
            writer.WriteFieldHeader(delta, expected, actual, WireType.TagDelimited);
        }
    }

    private static void Scalar<TWriter>(ref Writer<TWriter> writer, uint delta, bool malformed)
        where TWriter : IBufferWriter<byte>
    {
        if (malformed)
        {
            ReferenceCodec.MarkValueField(writer.Session);
            Unknown(ref writer, delta, WireType.VarInt);
            writer.WriteVarInt32(Number);
        }
        else
        {
            Int32Codec.WriteField(ref writer, delta, Number);
        }
    }
}
