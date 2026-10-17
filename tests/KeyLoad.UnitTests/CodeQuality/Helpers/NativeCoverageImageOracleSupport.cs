using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageOracleSupport
{
    private const int PermissionMask = 0x1ff;

    internal static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    internal static void RequireKeys(JsonElement value, params string[] expected)
    {
        var actual = value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Select(property => property.Name).ToArray()
            : Array.Empty<string>();
        Ensure(value.ValueKind == JsonValueKind.Object
            && actual.Length == expected.Length
            && actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "A native coverage image record has an unexpected field inventory.");
    }

    internal static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        Ensure(info.Exists && info.Length >= 0 && info.Length <= maximumBytes,
            "An observed image file exceeds its captured read bound.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Ensure(stream.Length >= 0 && stream.Length <= maximumBytes,
            "An opened image file exceeds its captured read bound.");
        var length = stream.Length;
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        Ensure(stream.ReadByte() == -1, "An image file grew beyond its captured read bound.");
        return bytes;
    }

    internal static string HashFile(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        Ensure(info.Exists && info.Length >= 0 && info.Length <= maximumBytes,
            "A materialized file exceeds its independently admitted size.");
        using var stream = File.OpenRead(path);
        Ensure(stream.Length == info.Length && stream.Length <= maximumBytes,
            "An opened materialized file changed beyond its independent size bound.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static int Mode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native coverage image permissions require a Unix filesystem.");
        }
        return (int)File.GetUnixFileMode(path) & PermissionMask;
    }
}
