using System.Buffers;
using System.Text.Json;
using KeyLoad.InternalSerialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace KeyLoad;

/// <summary>Uses the Orleans generated serializer for owned internal contracts.</summary>
public static class OrleansSerialization
{
    private static readonly ServiceProvider Services = CreateServices();
    internal static Serializer Serializer { get; } = Services.GetRequiredService<Serializer>();
    private static Serializer<RuntimePayload> PayloadSerializer { get; } = Services.GetRequiredService<Serializer<RuntimePayload>>();
    private static DeepCopier Copier { get; } = Services.GetRequiredService<DeepCopier>();

    /// <summary>Encodes one nonnull owned value through the generated Orleans codec.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The value to encode.</param>
    /// <returns>The complete generated binary payload.</returns>
    // Internal contracts are validated once where public input enters; owned bytes are not re-walked here.
    public static byte[] Serialize<T>(T value)
        => Serializer.SerializeToArray(new RuntimePayload { Version = PayloadVersion.Current, Value /* KLD0041: The current keyload.internal.value Id 1 slot retains runtime type headers during array encoding. */ = ((object?)value /* KLD0042: The existing generated object slot requires one value-type box here; changing it would change stored binary framing. */) });

    internal static T Copy<T>(T value) => Copier.Copy(value)!;

    /// <summary>Encodes one owned value into a caller-owned bounded stream.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The nonnull value to encode.</param>
    /// <param name="destination">The destination enforcing its write budget and cancellation.</param>
    public static void Serialize<T>(T value, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination) /* KLD0041: The standard CLR guard checks the caller-owned stream reference before writing the binary frame. */;
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value /* KLD0041: The current keyload.internal.value Id 1 slot retains runtime type headers during bounded-stream encoding. */ = ((object?)value /* KLD0042: The existing generated object slot requires one value-type box here; changing it would change stored binary framing. */) }, destination);
    }

    internal static void SerializeToWriter<T>(T value, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination) /* KLD0041: The standard CLR guard checks the bounded buffer-writer reference before frame encoding. */;
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value /* KLD0041: The current keyload.internal.value Id 1 slot retains runtime type headers during buffer-writer encoding. */ = ((object?)value /* KLD0042: The existing generated object slot requires one value-type box here; changing it would change stored binary framing. */) }, destination);
    }

    /// <summary>Decodes a generated contract and checks its owned application envelope.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The complete binary payload.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> value) => Deserialize<T>(value, ValidationProfile.Strict);

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, ValidationProfile profile)
    {
        using var session = Serializer.SessionPool.GetSession();
        return (T)(DeserializeOwned(value, session, typeof(T), profile == ValidationProfile.PublicInputElements) /* KLD0041: The current generated object field is decoded once and checked against this exact expected root type. */) /* KLD0042: The stored object slot has already passed the requested root-type check; this extracts T without re-encoding. */;
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session)
        => (T)(DeserializeOwned(value, session, typeof(T), false) /* KLD0041: The caller-owned session reads the current generated object field and validates its requested root type. */) /* KLD0042: The validated existing object-valued wire field is extracted as the requested T. */;

    internal static object /* KLD0041: This runtime-Type entry point returns the existing decoded wire slot after checking the registry-selected root type. */ Deserialize(ReadOnlySpan<byte> value, Type expectedType, ValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(expectedType) /* KLD0041: The standard CLR guard requires a real runtime Type for validation of the current object-valued wire slot. */;
        using var session = Serializer.SessionPool.GetSession();
        return DeserializeOwned(value, session, expectedType, profile == ValidationProfile.PublicInputElements) /* KLD0041: The runtime-Type caller receives the current object-valued wire field only after its expected-type validation. */;
    }

    private static object /* KLD0041: Current persisted RuntimePayload Id 1 is declared object; this shared reader validates that existing layout. */ DeserializeOwned(ReadOnlySpan<byte> value, SerializerSession session, Type expectedType,
        bool exactRoot)
    {
        var payload = ReadGeneratedPayload(value, session);
        if (payload.Version != PayloadVersion.Current)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, PayloadVersion.UnsupportedVersion);
        }
        if (payload.Value /* KLD0041: This reads the actual generated Id 1 field from the existing versioned binary envelope. */ is not { } result || !(expectedType.IsInstanceOfType(result) /* KLD0041: CLR runtime assignability validates the decoded existing object slot before any typed extraction. */)
            || exactRoot && result.GetType() /* KLD0041: Public admission requires the concrete decoded runtime type to equal the registry-selected root type. */ != expectedType)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
        return result;
    }

    private static RuntimePayload ReadGeneratedPayload(ReadOnlySpan<byte> value, SerializerSession session)
    {
        try
        {
            var reader = Reader.Create(value, session);
            var payload = PayloadSerializer.Deserialize(ref reader);
            if (payload is null || reader.Remaining != EmptyRemainingBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
            }
            return payload;
        }
        catch (Exception error) when (error is SerializerException or ArgumentException or IndexOutOfRangeException
            or OverflowException or InvalidCastException or FormatException or EndOfStreamException or TypeLoadException
            or InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }

    private const int EmptyRemainingBytes = 0;

    /// <summary>Checks a complete generated contract before admitting its effects.</summary>
    /// <param name="value">The owned application envelope to decode.</param>
    public static void Validate(ReadOnlySpan<byte> value) => Validate(value, typeof(object));

    internal static void Validate(ReadOnlySpan<byte> value, Type expectedRootType)
        => Validate(value, expectedRootType, ValidationProfile.Strict);

    internal static void Validate(ReadOnlySpan<byte> value, Type expectedRootType, ValidationProfile profile)
    {
        using var session = Serializer.SessionPool.GetSession();
        _ = (DeserializeOwned(value, session, expectedRootType, profile == ValidationProfile.PublicInputElements) /* KLD0041: Validation reads and checks the existing object-valued wire slot without exposing or reserializing it. */);
    }

    /// <summary>Measures the generated encoding through a counting destination.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The nonnull value to measure.</param>
    /// <returns>The exact encoded byte count.</returns>
    public static long Measure<T>(T value)
    {
        using var session = Serializer.SessionPool.GetSession();
        return Measure(value, session);
    }

    internal static long Measure<T>(T value, SerializerSession session)
    {
        using var buffer = new RuntimeCountingWriter();
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value /* KLD0041: The current keyload.internal.value Id 1 slot retains runtime type headers during exact encoded-size measurement. */ = ((object?)value /* KLD0042: The existing generated object slot requires one value-type box here; changing it would change stored binary framing. */) }, buffer, session);
        return buffer.Length;
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFieldCodec<ReadOnlyMemory<byte>>, ReadOnlyMemoryOfByteCodec>();
        services.AddSerializer(builder => builder.AddJsonSerializer(type => type == typeof(JsonElement)));
        return services.BuildServiceProvider();
    }
}
