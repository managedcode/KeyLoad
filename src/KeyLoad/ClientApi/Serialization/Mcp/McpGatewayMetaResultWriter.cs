using System.Text.Json;
using ModelContextProtocol;

namespace KeyLoad.Server;

/// <summary>Serializes one bounded metadata result without materializing an unbounded JSON string.</summary>
internal static class McpGatewayMetaResultWriter
{
    internal static byte[] Serialize<T>(T result, int maximumBytes) where T : class
    {
        ArgumentNullException.ThrowIfNull(result) /* KLD0041: BCL null validation accepts the result class-constrained metadata result reference through its object parameter. */;
        using var stream = new McpBoundedWriteStream(maximumBytes);
        JsonSerializer.Serialize(stream, result, McpJsonUtilities.DefaultOptions);
        return stream.ToOwnedArray();
    }
}
