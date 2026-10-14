using System.Text.Json;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsEventVectorExpiryJson
{
    private const string Prefix = "event-vector-expiry-";
    private static readonly string[] Fields =
    [nameof(RequestCqrsEventVectorExpiryRecord.Version), nameof(RequestCqrsEventVectorExpiryRecord.RequestId),
        nameof(RequestCqrsEventVectorExpiryRecord.CommandId), nameof(RequestCqrsEventVectorExpiryRecord.OriginalExpiresAt)];

    internal static RequestCqrsEventVectorExpiryRecord Read(RequestCqrsProbeJson json, ReadOnlySpan<byte> bytes)
    {
        var value = json.Read(bytes, Fields, json.EventVectorExpiryTypeInfo);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.RequestId == Guid.Empty
            || value.CommandId == Guid.Empty || value.OriginalExpiresAt == default
            || value.OriginalExpiresAt.Offset != TimeSpan.Zero)
        {
            throw Invalid();
        }
        return value;
    }

    internal static byte[] Write(RequestCqrsProbeJson json, RequestCqrsEventVectorExpiryRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json.EventVectorExpiryTypeInfo);
        _ = Read(json, bytes);
        return bytes;
    }

    internal static string Name(RequestCqrsEventVectorExpiryRecord value)
        => Prefix + value.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;

    internal static bool IsName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        {
            return false;
        }
        var text = name[Prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length];
        return Guid.TryParseExact(text, RequestCqrsProbeProtocol.SessionIdFormat, out var id)
            && id != Guid.Empty && id.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == text;
    }

    internal static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
