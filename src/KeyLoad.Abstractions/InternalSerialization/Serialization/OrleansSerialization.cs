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
    private static DeepCopier Copier { get; } = Services.GetRequiredService<DeepCopier>();

    /// <summary>Encodes one nonnull owned value through the generated Orleans codec.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The value to encode.</param>
    /// <returns>The complete generated binary payload.</returns>
    public static byte[] Serialize<T>(T value) => Serialize(value, ValidationProfile.Strict);

    internal static byte[] Serialize<T>(T value, ValidationProfile profile)
    {
        ContractValidation.Validate(value, profile);
        return Serializer.SerializeToArray(new RuntimePayload { Version = PayloadVersion.Current, Value = value });
    }

    internal static T Copy<T>(T value)
    {
        ContractValidation.Validate(value);
        var owned = Copier.Copy(value);
        ContractValidation.Validate(owned);
        return owned!;
    }

    /// <summary>Encodes one owned value into a caller-owned bounded stream.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The nonnull value to encode.</param>
    /// <param name="destination">The destination enforcing its write budget and cancellation.</param>
    public static void Serialize<T>(T value, Stream destination) => Serialize(value, destination, ValidationProfile.Strict);

    internal static void Serialize<T>(T value, Stream destination, ValidationProfile profile)
    {
        ContractValidation.Validate(value, profile);
        ArgumentNullException.ThrowIfNull(destination);
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value = value }, destination);
    }

    internal static void SerializeToWriter<T>(T value, IBufferWriter<byte> destination)
    {
        ContractValidation.Validate(value);
        ArgumentNullException.ThrowIfNull(destination);
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value = value }, destination);
    }

    /// <summary>Decodes a generated contract and checks its owned application envelope.</summary>
    /// <typeparam name="T">The attributed contract or scalar type.</typeparam>
    /// <param name="value">The complete binary payload.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> value) => Deserialize<T>(value, ValidationProfile.Strict);

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, ValidationProfile profile)
    {
        using var session = Serializer.SessionPool.GetSession();
        return (T)DeserializeOwned(value, session, typeof(T), profile == ValidationProfile.PublicInputElements, profile);
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> value, SerializerSession session)
        => (T)DeserializeOwned(value, session, typeof(T), false, ValidationProfile.Strict);

    internal static object Deserialize(ReadOnlySpan<byte> value, Type expectedType, ValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        using var session = Serializer.SessionPool.GetSession();
        return DeserializeOwned(value, session, expectedType, profile == ValidationProfile.PublicInputElements, profile);
    }

    private static object DeserializeOwned(ReadOnlySpan<byte> value, SerializerSession session, Type expectedType,
        bool exactRoot, ValidationProfile profile)
    {
        var payload = ReadGeneratedPayload(value, session);
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

    private static RuntimePayload ReadGeneratedPayload(ReadOnlySpan<byte> value, SerializerSession session)
    {
        try
        {
            var reader = Reader.Create(value, session);
            var payload = Serializer.GetSerializer<RuntimePayload>().Deserialize(ref reader);
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
        _ = DeserializeOwned(value, session, expectedRootType, false, profile);
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
        ContractValidation.Validate(value);
        using var buffer = new RuntimeCountingWriter();
        Serializer.Serialize(new RuntimePayload { Version = PayloadVersion.Current, Value = value }, buffer, session);
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
