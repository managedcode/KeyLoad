using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.InternalSerialization;

// Exact old CommandFingerprint object shape, with ordinal canonical property order and no JSON parsing.
internal static class NativeOperationFingerprint
{
    private const string IdProperty = "id";
    private const string KindProperty = "kind";
    private const string PayloadProperty = "payloadJson";
    private const string PrincipalProperty = "principalId";

    internal static string Compute(ReplicatedOperation operation)
        => Compute(operation, SerializationExecutionRegistration.Process);

    internal static string Compute(ReplicatedOperation operation, IOptions<SerializationExecutionOptions> executionOptions)
    {
        var chunkCharacters = ReadChunkCharacters(executionOptions);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var output = new CanonicalHashStream(hash);
        using (var writer = new Utf8JsonWriter(output))
        {
            Begin(writer, operation.Id, operation.Kind);
            WriteText(writer, operation.PayloadJson.AsSpan(), chunkCharacters);
            End(writer, operation.PrincipalId);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string Compute(Guid id, OperationKind kind, string principalId, ReadOnlySpan<byte> payloadJsonUtf8)
        => Compute(id, kind, principalId, payloadJsonUtf8, SerializationExecutionRegistration.Process);

    internal static string Compute(Guid id, OperationKind kind, string principalId, ReadOnlySpan<byte> payloadJsonUtf8,
        IOptions<SerializationExecutionOptions> executionOptions)
    {
        var chunkCharacters = ReadChunkCharacters(executionOptions);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var output = new CanonicalHashStream(hash);
        using (var writer = new Utf8JsonWriter(output))
        {
            Begin(writer, id, kind);
            WriteText(writer, payloadJsonUtf8, chunkCharacters);
            End(writer, principalId);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Begin(Utf8JsonWriter writer, Guid id, OperationKind kind)
    {
        writer.WriteStartObject();
        writer.WriteString(IdProperty, id);
        writer.WritePropertyName(KindProperty);
        JsonSerializer.Serialize(writer, kind, JsonDefaults.Options);
        writer.WritePropertyName(PayloadProperty);
    }

    private static void End(Utf8JsonWriter writer, string principalId)
    {
        writer.WriteString(PrincipalProperty, principalId);
        writer.WriteEndObject();
    }

    private static int ReadChunkCharacters(IOptions<SerializationExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        var configured = executionOptions.Value;
        configured.Validate();
        return configured.FingerprintChunkCharacters;
    }

    private static void WriteText(Utf8JsonWriter writer, ReadOnlySpan<byte> text, int chunkCharacters)
    {
        while (text.Length > chunkCharacters)
        {
            writer.WriteStringValueSegment(text[..chunkCharacters], false);
            writer.Flush();
            text = text[chunkCharacters..];
        }
        writer.WriteStringValueSegment(text, true);
    }

    private static void WriteText(Utf8JsonWriter writer, ReadOnlySpan<char> text, int chunkCharacters)
    {
        while (text.Length > chunkCharacters)
        {
            writer.WriteStringValueSegment(text[..chunkCharacters], false);
            writer.Flush();
            text = text[chunkCharacters..];
        }
        writer.WriteStringValueSegment(text, true);
    }
}
