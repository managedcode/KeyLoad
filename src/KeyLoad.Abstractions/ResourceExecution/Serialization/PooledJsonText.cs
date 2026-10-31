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

    public static T Deserialize<T>(string value) => (T)Deserialize(value, typeof(T));

    public static object Deserialize(string value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var buffer = Buffers.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            return JsonDefaults.Deserialize(buffer.AsSpan(0, written), type);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            Buffers.Return(buffer);
        }
    }
}
