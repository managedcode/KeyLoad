using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Produces owned canonical UTF-8 within an inclusive configured byte ceiling.</summary>
internal static class McpBoundedJson
{
    /// <summary>Serializes through the native canonical serializer and clears its private write capacity.</summary>
    /// <typeparam name="T">The canonical value type.</typeparam>
    /// <param name="value">The actual canonical value, including any configured defaults and converters.</param>
    /// <param name="maximumBytes">The positive inclusive byte ceiling for the returned payload.</param>
    /// <returns>An independent byte owner containing only the accepted canonical JSON.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The configured byte ceiling is not positive.</exception>
    /// <exception cref="KeyLoadException">The canonical JSON exceeds the configured byte ceiling.</exception>
    internal static byte[] Serialize<T>(T value, int maximumBytes)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        JsonSerializer.Serialize(stream, value, JsonDefaults.Options);
        return stream.ToOwnedArray();
    }
}
