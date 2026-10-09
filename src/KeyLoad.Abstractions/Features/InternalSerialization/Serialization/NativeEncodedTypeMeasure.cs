using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad;

/// <summary>Counts registered native encoded-type framing without materializing a value or granting authority.</summary>
public static class NativeEncodedTypeMeasure
{
    /// <summary>Measures a type in a fresh pooled session using the currently registered native type codec.</summary>
    /// <typeparam name="T">The closed contract whose type framing is required by an owned schema bound.</typeparam>
    /// <returns>Encoded-type bytes only, excluding field, reference, ordinal and payload framing.</returns>
    public static long Measure<T>()
    {
        var context = NativeSerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        using var buffer = new NativeCountingWriter();
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
