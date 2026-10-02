using System.Text.Json;

namespace KeyLoad.Replication;

/// <summary>Replica-only JSON transport preserving exact command UTF8 without nested JSON escape expansion.</summary>
public static class ReplicaProtocolCodec
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes typed replica contracts with the bounded operation payload encoding.</summary>
    /// <typeparam name="T">Replica contract type whose fields form the encoded message.</typeparam>
    /// <param name="value">Contract value to serialize with exact operation payload bytes.</param>
    /// <returns>The UTF8 replica message, with operation payloads encoded as base64.</returns>
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>Reads strict typed replica contracts and restores the exact original operation payload.</summary>
    /// <typeparam name="T">Replica contract type expected at the protocol boundary.</typeparam>
    /// <param name="bytes">Complete UTF8 replica message to decode.</param>
    /// <returns>The decoded contract, retaining original operation payload whitespace.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options);
        options.Converters.Insert(0, new ReplicaOperationJsonConverter());
        return options;
    }
}
