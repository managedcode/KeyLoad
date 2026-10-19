using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad;

/// <summary>Encodes owned internal contracts with the generated, versioned Orleans binary format.</summary>
public static class OrleansSerialization
{
    /// <summary>Encodes one nonnull owned value using a pooled execution session.</summary>
    /// <typeparam name="T">The attributed contract or execution scalar type.</typeparam>
    /// <param name="value">The value to encode.</param>
    /// <returns>The complete versioned binary payload.</returns>
    public static byte[] Serialize<T>(T value) => Serialize(value, ValidationProfile.Strict);

    internal static byte[] Serialize<T>(T value, ValidationProfile profile)
    {
        ContractValidation.Validate(value, profile);
        var context = SerializerProviders.Get(typeof(T));
        var serializer = context.Serializer;
        using var session = context.Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value = value }, ref writer);
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Encodes one owned value into a caller-owned stream through the execution serializer.</summary>
    /// <typeparam name="T">The attributed contract or execution scalar type.</typeparam>
    /// <param name="value">The nonnull value to encode.</param>
    /// <param name="destination">The destination enforcing its write budget and cancellation.</param>
    public static void Serialize<T>(T value, Stream destination) => Serialize(value, destination, ValidationProfile.Strict);

    internal static void Serialize<T>(T value, Stream destination, ValidationProfile profile)
    {
        ContractValidation.Validate(value, profile);
        ArgumentNullException.ThrowIfNull(destination);
        var context = SerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        context.Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value = value }, destination, session);
    }

    /// <summary>Decodes an owned value, rejecting unsupported versions, missing roots and trailing bytes.</summary>
    /// <typeparam name="T">The attributed contract or execution scalar type.</typeparam>
    /// <param name="value">The complete binary payload, which may be borrowed for this call.</param>
    /// <returns>The decoded value with independently owned retained data.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> value)
    {
        return Deserialize<T>(value, SerializerProviders.Get(typeof(T)));
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, ValidationProfile profile)
    {
        var context = SerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        return Deserialize<T>(value, session, profile);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerContext context)
    {
        using var session = context.Sessions.GetSession();
        return Deserialize<T>(value, session);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session)
        => Deserialize<T>(value, session, ValidationProfile.Strict);

    internal static object Deserialize(ReadOnlySpan<byte> value, Type expectedType, ValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        var context = SerializerProviders.Get(expectedType);
        using var session = context.Sessions.GetSession();
        return DeserializeOwned(value, session, expectedType, exactRoot: true, profile);
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session, ValidationProfile profile)
        => (T)DeserializeOwned(value, session, typeof(T), profile == ValidationProfile.PublicInputElements, profile);

    private static object DeserializeOwned(ReadOnlySpan<byte> value, SerializerSession session, Type expectedType,
        bool exactRoot, ValidationProfile profile)
    {
        try
        {
            RuntimePayloadSyntax.Validate(value, session, expectedType, exactRoot);
            session.Reset();
            var reader = Reader.Create(value, session);
            var field = reader.ReadFieldHeader();
            PayloadHeader.Validate(field);
            var payload = session.CodecProvider.GetCodec<RuntimePayload>().ReadValue(ref reader, field);
            if (payload is null || reader.Remaining != WireIdentities.EmptyRemainingBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
            }
            if (payload.Version != PayloadVersion.Current)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, PayloadVersion.UnsupportedVersion);
            }
            if (payload.Value is not { } result || !expectedType.IsInstanceOfType(result)
                || exactRoot && result.GetType() != expectedType)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
            }
            ContractValidation.Validate(result, profile);
            return result;
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }

    /// <summary>Checks execution envelope syntax without decoding the retained payload value.</summary>
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
        var context = SerializerProviders.Get(expectedRootType ?? typeof(object));
        using var session = context.Sessions.GetSession();
        try
        {
            RuntimePayloadSyntax.Validate(value, session, expectedRootType);
        }
        catch (Exception exception) when (IsMalformed(exception))
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }

    /// <summary>Measures the exact encoded payload through the same generated codec without retaining it.</summary>
    /// <typeparam name="T">The attributed contract or execution scalar type.</typeparam>
    /// <param name="value">The nonnull value to measure.</param>
    /// <returns>The encoded byte count, including the execution version envelope.</returns>
    public static long Measure<T>(T value)
    {
        var context = SerializerProviders.Get(typeof(T));
        using var session = context.Sessions.GetSession();
        return Measure(value, session);
    }

    internal static long Measure<T>(T value, SerializerSession session)
    {
        ContractValidation.Validate(value);
        using var buffer = new RuntimeCountingWriter();
        var writer = Writer.Create(buffer, session);
        session.CodecProvider.GetCodec<RuntimePayload>().WriteField(ref writer, WireIdentities.FirstFieldId, typeof(RuntimePayload),
            new RuntimePayload { Version = PayloadVersion.Current, Value = value });
        writer.Commit();
        return buffer.Length;
    }

    private static bool IsMalformed(Exception exception)
        => exception is SerializerException
            or ArgumentException or IndexOutOfRangeException or OverflowException or InvalidCastException
            or FormatException or EndOfStreamException or TypeLoadException
            || RuntimePayloadSyntax.IsReaderBufferFailure(exception);
}
