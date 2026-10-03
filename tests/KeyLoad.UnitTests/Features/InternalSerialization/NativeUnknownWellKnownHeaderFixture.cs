using System.Buffers;
using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativeUnknownWellKnownHeaderScope { Envelope, Value, Member, UnknownMember }

// The same official writer authors complete valid controls and unknown type metadata.
internal static class NativeUnknownWellKnownHeaderFixture
{
    internal const string Alias = "keyload.tests.unknown-well-known-header.v1";
    internal const string Canary = "private-unknown-header-canary";
    internal const uint UnknownTypeId = uint.MaxValue;
    internal const int Number = 42;
    private const string RegisteredAdversarialId = "The adversarial type ID must be unregistered.";

    internal static byte[] Encode(NativeUnknownWellKnownHeaderScope scope, uint delta, bool malformed)
    {
        using var session = NativeSerializerProviders.Get(typeof(NativeUnknownWellKnownHeaderRecord)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            ReferenceCodec.MarkValueField(session);
            Header(ref writer, 0, typeof(NativePayload), typeof(NativePayload),
                scope == NativeUnknownWellKnownHeaderScope.Envelope && malformed);
            UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
            ReferenceCodec.MarkValueField(session);
            Header(ref writer, 1, typeof(object), typeof(NativeUnknownWellKnownHeaderRecord),
                scope == NativeUnknownWellKnownHeaderScope.Value && malformed);
            writer.WriteEndBase();
            StringCodec.WriteField(ref writer, 0, Canary);
            Scalar(ref writer, 1, scope == NativeUnknownWellKnownHeaderScope.Member && malformed);
            if (scope == NativeUnknownWellKnownHeaderScope.UnknownMember)
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

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeUnknownWellKnownHeaderFixture.Alias)]
internal sealed record NativeUnknownWellKnownHeaderRecord(
    [property: global::Orleans.Id(0)] string Text,
    [property: global::Orleans.Id(1)] int Number);
