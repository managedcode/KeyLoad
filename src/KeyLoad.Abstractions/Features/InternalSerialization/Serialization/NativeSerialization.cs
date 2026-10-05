using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad;

/// <summary>Encodes owned internal contracts with the generated, versioned Orleans binary format.</summary>
public static class NativeSerialization
{
    /// <summary>Encodes one nonnull owned value using a pooled native session.</summary>
    /// <typeparam name="T">The attributed contract or native scalar type.</typeparam>
    /// <param name="value">The value to encode.</param>
    /// <returns>The complete versioned binary payload.</returns>
    public static byte[] Serialize<T>(T value) => Serialize(value, NativeValidationProfile.Strict);

    internal static byte[] Serialize<T>(T value, NativeValidationProfile profile)
    {
        NativeContractValidation.Validate(value, profile);
        var context = NativeSerializerProviders.Get(typeof(T));
        var serializer = context.Serializer;
        using var session = context.Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            serializer.Serialize(new NativePayload { Version = NativePayloadVersion.Current, Value = value }, ref writer);
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Encodes one owned value into a caller-owned stream through the native serializer.</summary>
    /// <typeparam name="T">The attributed contract or native scalar type.</typeparam>
    /// <param name="value">The nonnull value to encode.</param>
    /// <param name="destination">The destination enforcing its write budget and cancellation.</param>
    public static void Serialize<T>(T value, Stream destination) => Serialize(value, destination, NativeValidationProfile.Strict);

    internal static void Serialize<T>(T value, Stream destination, NativeValidationProfile profile)
    {
        NativeContractValidation.Validate(value, profile);
        ArgumentNullException.ThrowIfNull(destination);
        var context = NativeSerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        context.Serializer.Serialize(new NativePayload { Version = NativePayloadVersion.Current, Value = value }, destination, session);
    }

    /// <summary>Decodes an owned value, rejecting unsupported versions, missing roots and trailing bytes.</summary>
    /// <typeparam name="T">The attributed contract or native scalar type.</typeparam>
    /// <param name="value">The complete binary payload, which may be borrowed for this call.</param>
    /// <returns>The decoded value with independently owned retained data.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> value)
    {
        return Deserialize<T>(value, NativeSerializerProviders.Get(typeof(T)));
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, NativeValidationProfile profile)
    {
        var context = NativeSerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        return Deserialize<T>(value, session, profile);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, NativeSerializerContext context)
    {
        using var session = context.Sessions.GetSession();
        return Deserialize<T>(value, session);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session)
        => Deserialize<T>(value, session, NativeValidationProfile.Strict);

    private static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session, NativeValidationProfile profile)
    {
        try
        {
            NativePayloadSyntax.Validate(value, session, typeof(T), profile == NativeValidationProfile.PublicInputElements);
            session.Reset();
            var reader = Reader.Create(value, session);
            var field = reader.ReadFieldHeader();
            NativePayloadHeader.Validate(field);
            var payload = session.CodecProvider.GetCodec<NativePayload>().ReadValue(ref reader, field);
            if (payload is null || reader.Remaining != NativeWireIdentities.EmptyRemainingBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
            }
            if (payload.Version != NativePayloadVersion.Current)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, NativePayloadVersion.UnsupportedVersion);
            }
            if (payload.Value is not T result
                || profile == NativeValidationProfile.PublicInputElements && result.GetType() != typeof(T))
            {
                throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
            }
            NativeContractValidation.Validate(result, profile);
            return result;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }

    /// <summary>Checks native envelope syntax without decoding the retained payload value.</summary>
    /// <param name="value">The complete versioned binary payload.</param>
    /// <remarks>Typed semantics, authorization and implicit-field UTF8 validation remain at their owning boundaries.</remarks>
    public static void Validate(ReadOnlySpan<byte> value) => ValidateSyntax(value, null);

    internal static void Validate(ReadOnlySpan<byte> value, Type expectedRootType)
    {
        ArgumentNullException.ThrowIfNull(expectedRootType);
        ValidateSyntax(value, expectedRootType);
    }

    private static void ValidateSyntax(ReadOnlySpan<byte> value, Type? expectedRootType)
    {
        var context = NativeSerializerProviders.Get(expectedRootType ?? typeof(object));
        using var session = context.Sessions.GetSession();
        try
        {
            NativePayloadSyntax.Validate(value, session, expectedRootType);
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }

    /// <summary>Measures the exact encoded payload through the same generated codec without retaining it.</summary>
    /// <typeparam name="T">The attributed contract or native scalar type.</typeparam>
    /// <param name="value">The nonnull value to measure.</param>
    /// <returns>The encoded byte count, including the native version envelope.</returns>
    public static long Measure<T>(T value)
    {
        var context = NativeSerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        return Measure(value, session);
    }

    internal static long Measure<T>(T value, SerializerSession session)
    {
        NativeContractValidation.Validate(value);
        using var buffer = new NativeCountingWriter();
        var writer = Writer.Create(buffer, session);
        session.CodecProvider.GetCodec<NativePayload>().WriteField(ref writer, NativeWireIdentities.FirstFieldId, typeof(NativePayload),
            new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        writer.Commit();
        return buffer.Length;
    }

    private static bool IsMalformed(Exception exception)
        => exception is SerializerException
            or ArgumentException or IndexOutOfRangeException or OverflowException or InvalidCastException
            or FormatException or EndOfStreamException or TypeLoadException
            || NativePayloadSyntax.IsReaderBufferFailure(exception);
}
