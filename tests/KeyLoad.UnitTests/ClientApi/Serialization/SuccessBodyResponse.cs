using System.Text;
using System.Text.Json;
using P = KeyLoad.UnitTests.Features.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SuccessBodyResponse
{
    internal static byte[] Compose<T>(T runtimeActualValue, string unusable, bool appendPrefix,
        int maximumResponseBytes)
    {
        var suffix = Encoding.UTF8.GetBytes(unusable);
        if (!appendPrefix)
        {
            return suffix;
        }
        var prefix = JsonSerializer.SerializeToUtf8Bytes(runtimeActualValue, JsonDefaults.Options);
        if (checked(prefix.Length + suffix.Length) >= maximumResponseBytes)
        {
            throw new InvalidOperationException(P.Failure);
        }
        return [.. prefix, .. suffix];
    }
}
