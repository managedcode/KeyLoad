using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Deliberately malformed collections use the official native writer and scalar codecs.
internal static class NativeWireFixture
{
    internal static byte[] Collection(Type type, uint count, int items, bool duplicateCount = false, bool omitCount = false)
    {
        var context = NativeSerializerProviders.Get(type);
        using var session = context.Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            Start(ref writer, type);
            WriteCollection(ref writer, type, count, items, duplicateCount, omitCount);
            writer.WriteEndObject();
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    internal static byte[] NestedVector(Type type)
    {
        using var session = NativeSerializerProviders.Get(type).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            Start(ref writer, type);
            writer.WriteEndBase();
            if (type == typeof(PutVector))
            {
                StringCodec.WriteField(ref writer, 0, "vectors");
                writer.WriteEndBase();
                writer.WriteEndBase();
            }
            writer.WriteFieldHeaderExpected(3, WireType.TagDelimited);
            writer.WriteFieldHeaderExpected(0, WireType.TagDelimited);
            UInt32Codec.WriteField(ref writer, 0, 2);
            FloatCodec.WriteField(ref writer, 1, 1f);
            writer.WriteEndObject();
            writer.WriteEndObject();
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

    internal static byte[] PrincipalBodyArray()
    {
        using var session = NativeSerializerProviders.Get(typeof(PrincipalRecord)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            Start(ref writer, typeof(PrincipalRecord));
            writer.WriteEndBase();
            writer.WriteFieldHeaderExpected(6, WireType.TagDelimited);
            writer.WriteFieldHeaderExpected(0, WireType.TagDelimited);
            UInt32Codec.WriteField(ref writer, 0, 2);
            StringCodec.WriteField(ref writer, 1, "one");
            writer.WriteEndObject();
            writer.WriteEndObject();
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

    internal static byte[] Tags(int depth, uint? reference = null)
    {
        using var session = NativeSerializerProviders.Get(typeof(OutboxHead)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            Start(ref writer, typeof(OutboxHead));
            for (var index = 1; index < depth; index++)
            {
                writer.WriteFieldHeader(10, null, typeof(OutboxHead), WireType.TagDelimited);
            }
            if (reference is { } target)
            {
                writer.WriteFieldHeaderExpected(10, WireType.Reference);
                writer.WriteVarUInt32(target);
            }
            for (var index = 0; index < depth; index++)
            {
                writer.WriteEndObject();
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

    private static void Start<TBuffer>(ref Writer<TBuffer> writer, Type type) where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        writer.WriteFieldHeader(0, typeof(NativePayload), typeof(NativePayload), WireType.TagDelimited);
        UInt32Codec.WriteField(ref writer, 0, NativePayloadVersion.Current);
        writer.WriteFieldHeader(1, typeof(object), type, WireType.TagDelimited);
    }

    private static void WriteCollection<TBuffer>(ref Writer<TBuffer> writer, Type type, uint count, int items, bool duplicate, bool omitCount) where TBuffer : System.Buffers.IBufferWriter<byte>
    {
        var dictionary = type == typeof(Dictionary<string, string>);
        if (!omitCount)
        {
            UInt32Codec.WriteField(ref writer, dictionary ? 1u : 0u, count);
        }
        if (duplicate)
        {
            UInt32Codec.WriteField(ref writer, 0, count);
        }
        for (var index = 0; index < items; index++)
        {
            var firstDelta = dictionary && omitCount ? 2u : 1u;
            var delta = index == 0 ? firstDelta : 0u;
            if (dictionary)
            {
                StringCodec.WriteField(ref writer, delta, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                FloatCodec.WriteField(ref writer, delta, 1f);
            }
        }
    }
}
