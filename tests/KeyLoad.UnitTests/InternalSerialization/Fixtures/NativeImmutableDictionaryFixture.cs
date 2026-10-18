using System.Collections.Immutable;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Official native headers/scalars author malformed frames, never an alternate decoder.
internal static class NativeImmutableDictionaryFixture
{
    internal static byte[] Encode(NativeImmutableDictionaryFault fault)
    {
        var type = typeof(ImmutableDictionary<string, string>);
        using var session = NativeSerializerProviders.Get(type).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            writer.WriteFieldHeader(0, typeof(NativePayload), typeof(NativePayload), WireType.TagDelimited);
            UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
            writer.WriteFieldHeader(1, typeof(object), type, WireType.TagDelimited);
            WriteValuesField(ref writer, fault);
            if (fault == NativeImmutableDictionaryFault.AlienMember)
            {
                UInt32Codec.WriteField(ref writer, 2, 1);
            }

            if (fault == NativeImmutableDictionaryFault.BaseScope)
            {
                writer.WriteEndBase();
            }

            if (fault == NativeImmutableDictionaryFault.UnknownComparer)
            {
                writer.WriteFieldHeader(1, typeof(IEqualityComparer<string>), typeof(NativeImmutableDictionaryUnknownComparer), WireType.TagDelimited);
                writer.WriteEndObject();
            }
            if (fault == NativeImmutableDictionaryFault.TypedNullComparer)
            {
                writer.WriteFieldHeader(1, typeof(IEqualityComparer<string>), typeof(NativeImmutableDictionaryUnknownComparer), WireType.Reference);
                writer.WriteVarUInt32(0);
            }
            if (fault == NativeImmutableDictionaryFault.DuplicateComparer)
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

    private static void WriteValuesField<TBuffer>(ref Writer<TBuffer> writer, NativeImmutableDictionaryFault fault)
        where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        if (fault == NativeImmutableDictionaryFault.NullValues)
        {
            writer.WriteFieldHeaderExpected(0, WireType.Reference);
            writer.WriteVarUInt32(0);
        }
        else if (fault != NativeImmutableDictionaryFault.MissingValues)
        {
            Values(ref writer, fault);
            if (fault == NativeImmutableDictionaryFault.DuplicateValues)
            {
                Values(ref writer, fault);
            }
        }
    }

    private static void Values<TBuffer>(ref Writer<TBuffer> writer, NativeImmutableDictionaryFault fault)
        where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        writer.WriteFieldHeaderExpected(0, WireType.TagDelimited);
        if (fault == NativeImmutableDictionaryFault.UnknownKeyComparer)
        {
            writer.WriteFieldHeader(0, typeof(IEqualityComparer<string>), typeof(NativeImmutableDictionaryUnknownComparer), WireType.TagDelimited);
            writer.WriteEndObject();
        }
        UInt32Codec.WriteField(ref writer, 1, fault == NativeImmutableDictionaryFault.OversizedCount ? uint.MaxValue : 1u);
        StringCodec.WriteField(ref writer, 1, "key");
        if (fault != NativeImmutableDictionaryFault.IncompletePairs)
        {
            StringCodec.WriteField(ref writer, 0, "value");
        }

        writer.WriteEndObject();
    }
}
