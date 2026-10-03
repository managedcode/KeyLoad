using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeWireOpaqueFixture
{
    internal static byte[] ReferencedVector(bool reference = true)
    {
        using var session = NativeSerializerProviders.Get(typeof(PutVector)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            writer.WriteFieldHeader(0, typeof(NativePayload), typeof(NativePayload), WireType.TagDelimited);
            ReferenceCodec.RecordObject(session, new NativePayload());
            UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
            writer.WriteFieldHeader(1, typeof(object), typeof(PutVector), WireType.TagDelimited);
            ReferenceCodec.RecordObject(session, new object());
            WriteBase(ref writer);
            WriteDerived(ref writer, reference);
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

    private static void WriteBase<TBuffer>(ref Writer<TBuffer> writer) where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        writer.WriteEndBase();
        StringCodec.WriteField(ref writer, 0, "vectors");
        // Unknown omitted-type field at native reference ID5. Its true array type is only
        // inferred by Orleans if the later Values surrogate replays this opaque marker.
        writer.WriteFieldHeaderExpected(1, WireType.TagDelimited);
        ReferenceCodec.RecordObject(writer.Session, new object());
        UInt32Codec.WriteField(ref writer, 0, 2);
        FloatCodec.WriteField(ref writer, 1, 1f);
        writer.WriteEndObject();
        writer.WriteEndBase();
    }

    private static void WriteDerived<TBuffer>(ref Writer<TBuffer> writer, bool reference) where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        writer.WriteEndBase();
        StringCodec.WriteField(ref writer, 0, "vectors");
        StringCodec.WriteField(ref writer, 1, "doc");
        StringCodec.WriteField(ref writer, 1, "embedding");
        writer.WriteFieldHeaderExpected(1, WireType.TagDelimited);
        ReferenceCodec.MarkValueField(writer.Session);
        if (reference)
        {
            writer.WriteFieldHeaderExpected(0, WireType.Reference);
            ReferenceCodec.MarkValueField(writer.Session);
            writer.WriteVarUInt32(5);
        }
        else
        {
            writer.Session.CodecProvider.GetCodec<float[]>().WriteField(ref writer, 0, typeof(float[]), [1f, 2f]);
        }
        writer.WriteEndObject();
        var space = new VectorSpace("space", 2, DistanceMetric.Cosine, "model", "1");
        writer.Session.CodecProvider.GetCodec<VectorSpace>().WriteField(ref writer, 1, typeof(VectorSpace), space);
        Int64Codec.WriteField(ref writer, 1, 1);
    }
}
