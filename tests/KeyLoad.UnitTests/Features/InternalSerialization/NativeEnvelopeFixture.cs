using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Author malformed envelopes with native header/value codecs; never duplicate Orleans varints.
internal static class NativeEnvelopeFixture
{
    internal static byte[] Encode(SerializerSessionPool sessions, NativeEnvelopeFault fault)
    {
        using var session = sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            var rootType = fault == NativeEnvelopeFault.WrongRoot ? typeof(OutboxHead) : typeof(NativePayload);
            writer.WriteFieldHeader(0, typeof(NativePayload), rootType, WireType.TagDelimited);
            ReferenceCodec.RecordObject(session, new NativePayload());
            if (fault != NativeEnvelopeFault.MissingVersion)
            {
                UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
            }
            if (fault == NativeEnvelopeFault.DuplicateVersion)
            {
                UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
            }
            if (fault != NativeEnvelopeFault.MissingValue)
            {
                ObjectCodec.WriteField(ref writer, 1, typeof(object), new OutboxHead(1, 1, 1, 1));
            }
            if (fault == NativeEnvelopeFault.DuplicateValue)
            {
                ObjectCodec.WriteField(ref writer, 0, typeof(object), new OutboxHead(1, 1, 1, 1));
            }
            if (fault == NativeEnvelopeFault.UnknownField)
            {
                UInt32Codec.WriteField(ref writer, 1, 1);
            }
            writer.WriteEndObject();
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }
}

internal enum NativeEnvelopeFault { WrongRoot, MissingVersion, MissingValue, DuplicateVersion, DuplicateValue, UnknownField }
