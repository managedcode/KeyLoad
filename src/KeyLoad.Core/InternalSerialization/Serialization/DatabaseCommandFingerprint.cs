using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.InternalSerialization;

// The original public input is fingerprinted without reparsing or changing user content.
internal static class DatabaseCommandFingerprint
{
    private const int ChunkCharacters = 4096;
    private const string IdProperty = "id";
    private const string KindProperty = "kind";
    private const string PayloadProperty = "payloadJson";
    private const string PrincipalProperty = "principalId";

    internal static string Compute(Command<DatabaseCommand> operation) => operation.Value!.Fingerprint;

    internal static string Compute(Guid id, OperationKind kind, string principalId, string payloadJson)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var output = new CanonicalHashStream(hash);
        using (var writer = new Utf8JsonWriter(output))
        {
            Begin(writer, id, kind);
            WriteText(writer, payloadJson.AsSpan());
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

    private static void WriteText(Utf8JsonWriter writer, ReadOnlySpan<char> text)
    {
        while (text.Length > ChunkCharacters)
        {
            writer.WriteStringValueSegment(text[..ChunkCharacters], false);
            writer.Flush();
            text = text[ChunkCharacters..];
        }
        writer.WriteStringValueSegment(text, true);
    }
}
