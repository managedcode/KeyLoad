using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Author malformed envelopes with execution header/value codecs; never duplicate Orleans varints.
internal static class EnvelopeFixture
{
    internal static byte[] Encode(SerializerSessionPool sessions, EnvelopeFault fault)
    {
        using var session = sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            var rootType = fault == EnvelopeFault.WrongRoot ? typeof(OutboxHead) : typeof(RuntimePayload);
            writer.WriteFieldHeader(0, typeof(RuntimePayload), rootType, WireType.TagDelimited);
            ReferenceCodec.RecordObject(session, new RuntimePayload());
            if (fault != EnvelopeFault.MissingVersion)
            {
                UInt32Codec.WriteField(ref writer, 0, PayloadVersion.Current);
            }
            if (fault == EnvelopeFault.DuplicateVersion)
            {
                UInt32Codec.WriteField(ref writer, 0, PayloadVersion.Current);
            }
            if (fault != EnvelopeFault.MissingValue)
            {
                ObjectCodec.WriteField(ref writer, 1, typeof(object), new OutboxHead(1, 1, 1, 1));
            }
            if (fault == EnvelopeFault.DuplicateValue)
            {
                ObjectCodec.WriteField(ref writer, 0, typeof(object), new OutboxHead(1, 1, 1, 1));
            }
            if (fault == EnvelopeFault.UnknownField)
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
