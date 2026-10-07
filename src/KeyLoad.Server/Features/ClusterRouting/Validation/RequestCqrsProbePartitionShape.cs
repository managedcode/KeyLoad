using System.Text;
using System.Text.Json;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbePartitionShape
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] Fields = [nameof(PartitionRef.TenantId), nameof(PartitionRef.DatabaseId),
        nameof(PartitionRef.TransactionDomainId), nameof(PartitionRef.PartitionKey)];
    internal static void Read(ref Utf8JsonReader reader, int maximumComponentBytes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped)
            { throw Invalid(); }
            var name = reader.GetString();
            if (name is null || !Fields.Contains(name, StringComparer.Ordinal) || !seen.Add(name)
                || !reader.Read() || reader.TokenType != JsonTokenType.String)
            { throw Invalid(); }
            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value) || ComponentBytes(value) > maximumComponentBytes)
            { throw Invalid(); }
        }
        if (reader.TokenType != JsonTokenType.EndObject || seen.Count != Fields.Length)
        { throw Invalid(); }
    }
    private static int ComponentBytes(string value)
    {
        try
        { return Utf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
