using System.Text.Json;
using ModelContextProtocol;

namespace KeyLoad.Server;

/// <summary>Serializes one bounded metadata result without materializing an unbounded JSON string.</summary>
internal static class McpGatewayMetaResultWriter
{
    internal static byte[] Serialize<T>(T result, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var stream = new McpBoundedWriteStream(maximumBytes);
        JsonSerializer.Serialize(stream, result, McpJsonUtilities.DefaultOptions);
        return stream.ToOwnedArray();
    }
}
