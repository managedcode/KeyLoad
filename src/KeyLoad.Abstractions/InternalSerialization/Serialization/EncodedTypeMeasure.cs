using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad;

/// <summary>Counts registered execution encoded-type framing without materializing a value or granting authority.</summary>
public static class EncodedTypeMeasure
{
    /// <summary>Measures a type in a fresh pooled session using the currently registered execution type codec.</summary>
    /// <typeparam name="T">The closed contract whose type framing is required by an owned schema bound.</typeparam>
    /// <returns>Encoded-type bytes only, excluding field, reference, ordinal and payload framing.</returns>
    public static long Measure<T>()
    {
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        using var buffer = new RuntimeCountingWriter();
        var writer = Writer.Create(buffer, session);
        try
        {
            session.TypeCodec.WriteEncodedType(ref writer, typeof(T));
            writer.Commit();
            return buffer.Length;
        }
        finally
        {
            writer.Dispose();
        }
    }
}
