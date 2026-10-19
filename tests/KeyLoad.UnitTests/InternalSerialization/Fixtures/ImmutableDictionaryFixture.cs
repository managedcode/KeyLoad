using System.Collections.Immutable;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Official execution headers/scalars author malformed frames, never an alternate decoder.
internal static class ImmutableDictionaryFixture
{
    internal static byte[] Encode(ImmutableDictionaryFault fault)
    {
        var type = typeof(ImmutableDictionary<string, string>);
        using var session = SerializerProviders.Get(type).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            writer.WriteFieldHeader(0, typeof(RuntimePayload), typeof(RuntimePayload), WireType.TagDelimited);
            UInt32Codec.WriteField(ref writer, 0, PayloadVersion.Current);
            writer.WriteFieldHeader(1, typeof(object), type, WireType.TagDelimited);
            WriteValuesField(ref writer, fault);
            if (fault == ImmutableDictionaryFault.AlienMember)
            {
                UInt32Codec.WriteField(ref writer, 2, 1);
            }

            if (fault == ImmutableDictionaryFault.BaseScope)
            {
                writer.WriteEndBase();
            }

            if (fault == ImmutableDictionaryFault.UnknownComparer)
            {
                writer.WriteFieldHeader(1, typeof(IEqualityComparer<string>), typeof(ImmutableDictionaryUnknownComparer), WireType.TagDelimited);
                writer.WriteEndObject();
            }
            if (fault == ImmutableDictionaryFault.TypedNullComparer)
            {
                writer.WriteFieldHeader(1, typeof(IEqualityComparer<string>), typeof(ImmutableDictionaryUnknownComparer), WireType.Reference);
                writer.WriteVarUInt32(0);
            }
            if (fault == ImmutableDictionaryFault.DuplicateComparer)
            {
                ReferenceCodec.WriteNullReference(ref writer, 1);
                ReferenceCodec.WriteNullReference(ref writer, 0);
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

    private static void WriteValuesField<TBuffer>(ref Writer<TBuffer> writer, ImmutableDictionaryFault fault)
        where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        if (fault == ImmutableDictionaryFault.NullValues)
        {
            writer.WriteFieldHeaderExpected(0, WireType.Reference);
            writer.WriteVarUInt32(0);
        }
        else if (fault != ImmutableDictionaryFault.MissingValues)
        {
            Values(ref writer, fault);
            if (fault == ImmutableDictionaryFault.DuplicateValues)
            {
                Values(ref writer, fault);
            }
        }
    }

    private static void Values<TBuffer>(ref Writer<TBuffer> writer, ImmutableDictionaryFault fault)
        where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        writer.WriteFieldHeaderExpected(0, WireType.TagDelimited);
        if (fault == ImmutableDictionaryFault.UnknownKeyComparer)
        {
            writer.WriteFieldHeader(0, typeof(IEqualityComparer<string>), typeof(ImmutableDictionaryUnknownComparer), WireType.TagDelimited);
            writer.WriteEndObject();
        }
        UInt32Codec.WriteField(ref writer, 1, fault == ImmutableDictionaryFault.OversizedCount ? uint.MaxValue : 1u);
        StringCodec.WriteField(ref writer, 1, "key");
        if (fault != ImmutableDictionaryFault.IncompletePairs)
        {
            StringCodec.WriteField(ref writer, 0, "value");
        }

        writer.WriteEndObject();
    }
}
