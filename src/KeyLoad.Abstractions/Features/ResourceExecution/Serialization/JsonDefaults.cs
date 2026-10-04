using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Features.ResourceExecution;

namespace KeyLoad;

/// <summary>Provides the canonical JSON options and persistence serialization helpers.</summary>
public static class JsonDefaults
{
    private const string MissingRecordMessage = "A persisted record has no value.";
    /// <summary>Gets the canonical serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 64,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new StrictByteMemoryJsonConverter());
        options.Converters.Add(new StrictImmutableArrayJsonConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
    /// <summary>Serializes a value using the canonical KeyLoad JSON options.</summary>
    /// <typeparam name="T">Specifies the value type.</typeparam>
    /// <param name="value">Provides the value to serialize.</param>
    /// <returns>The serialized UTF-8 JSON bytes.</returns>
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    /// <summary>Deserializes persisted JSON using the canonical options and rejects an empty result.</summary>
    /// <typeparam name="T">Specifies the type to deserialize.</typeparam>
    /// <param name="value">Provides the persisted UTF-8 JSON bytes.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="KeyLoadException">The persisted value is missing or corrupt.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw Errors.Fail(ErrorCode.Corruption, MissingRecordMessage);

    /// <summary>Deserializes canonical JSON text with a scoped, cleared UTF-8 input loan.</summary>
    /// <typeparam name="T">Specifies the type to deserialize.</typeparam>
    /// <param name="value">Provides JSON text with the existing UTF-8 replacement encoding semantics.</param>
    /// <returns>The deserialized value, owning any retained data independently of the input loan.</returns>
    /// <exception cref="ArgumentNullException">The JSON text argument is null.</exception>
    /// <exception cref="KeyLoadException">The serialized value is missing or corrupt.</exception>
    public static T Deserialize<T>(string value) => PooledJsonText.Deserialize<T>(value);
}
