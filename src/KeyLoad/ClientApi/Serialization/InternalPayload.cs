using KeyLoad.InternalSerialization;

namespace KeyLoad.Server;

internal static class InternalPayload
{
    internal static byte[] Serialize<T>(T value, int maximumBytes)
        => Serialize(value, maximumBytes, ValidationProfile.Strict);

    internal static byte[] SerializePublicInput<T>(T value, int maximumBytes)
        => Serialize(value, maximumBytes, ValidationProfile.PublicInputElements);

    private static byte[] Serialize<T>(T value, int maximumBytes, ValidationProfile profile)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        OrleansSerialization.Serialize(value, stream, profile);
        return stream.ToOwnedArray();
    }
}
