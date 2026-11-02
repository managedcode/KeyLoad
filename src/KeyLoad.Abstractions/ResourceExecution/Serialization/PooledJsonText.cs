using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.ResourceExecution;

/// <summary>Owns a bounded reusable UTF-8 input loan for canonical JSON text decoding.</summary>
internal static class PooledJsonText
{
    private const int MaximumRetainedArrayBytes = 262_144;
    private const int MaximumArraysPerBucket = 2;
    private static readonly ArrayPool<byte> Buffers = ArrayPool<byte>.Create(
        MaximumRetainedArrayBytes, MaximumArraysPerBucket);

    public static T Deserialize<T>(string value)
    {
        ArgumentNullException.ThrowIfNull(value) /* KLD0041: The standard CLR guard checks the typed JSON string reference before renting its UTF-8 loan. */;
        var buffer = Buffers.Rent(Encoding.UTF8.GetByteCount(value));
        try
        {
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            return JsonDefaults.Deserialize<T>(buffer.AsSpan(0, written));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            Buffers.Return(buffer);
        }
    }

    public static object /* KLD0041: The registry-selected runtime Type requires the native JSON object result; the generic overload remains fully typed. */ Deserialize(string value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value) /* KLD0041: The standard CLR guard checks the runtime-Type JSON input string before a bounded UTF-8 loan. */;
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = Buffers.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            return JsonDefaults.Deserialize(buffer.AsSpan(0, written), type) /* KLD0041: The runtime-Type JSON entry point consumes the borrowed bytes while retaining its native decoded result. */;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            Buffers.Return(buffer);
        }
    }
}
