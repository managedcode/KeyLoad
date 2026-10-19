using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Server;

internal static class InternalNativePayload
{
    internal static byte[] Serialize<T>(T value, int maximumBytes)
        => Serialize(value, maximumBytes, NativeValidationProfile.Strict);

    internal static byte[] SerializePublicInput<T>(T value, int maximumBytes)
        => Serialize(value, maximumBytes, NativeValidationProfile.PublicInputElements);

    private static byte[] Serialize<T>(T value, int maximumBytes, NativeValidationProfile profile)
    {
        using var stream = new McpBoundedWriteStream(maximumBytes);
        NativeSerialization.Serialize(value, stream, profile);
        return stream.ToOwnedArray();
    }
}
