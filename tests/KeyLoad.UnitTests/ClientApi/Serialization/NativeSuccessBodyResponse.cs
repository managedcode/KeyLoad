using System.Text;
using System.Text.Json;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeSuccessBodyResponse
{
    internal static byte[] Compose<T>(T actualNativeValue, string unusable, bool appendNativePrefix,
        int maximumResponseBytes)
    {
        var suffix = Encoding.UTF8.GetBytes(unusable);
        if (!appendNativePrefix)
        {
            return suffix;
        }
        var prefix = JsonSerializer.SerializeToUtf8Bytes(actualNativeValue, JsonDefaults.Options);
        if (checked(prefix.Length + suffix.Length) >= maximumResponseBytes)
        {
            throw new InvalidOperationException(P.Failure);
        }
        return [.. prefix, .. suffix];
    }
}
