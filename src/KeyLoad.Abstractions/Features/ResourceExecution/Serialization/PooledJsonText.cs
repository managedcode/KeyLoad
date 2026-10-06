using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Features.ResourceExecution;

/// <summary>Owns a bounded reusable UTF-8 input loan for canonical JSON text decoding.</summary>
internal static class PooledJsonText
{
    private static readonly Lazy<Owner> Shared = new(() => new(SerializationExecutionRegistration.Process));

    public static T Deserialize<T>(string value) => Shared.Value.Deserialize<T>(value);

    internal sealed class Owner
    {
        private const int FirstWrittenByte = 0;
        private readonly ArrayPool<byte> buffers;

        internal Owner(IOptions<SerializationExecutionOptions> executionOptions)
        {
            ArgumentNullException.ThrowIfNull(executionOptions);
            var configured = executionOptions.Value;
            configured.Validate();
            buffers = ArrayPool<byte>.Create(configured.JsonTextMaximumRetainedArrayBytes,
                configured.JsonTextMaximumArraysPerBucket);
        }

        internal T Deserialize<T>(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var buffer = buffers.Rent(byteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
                return JsonDefaults.Deserialize<T>(buffer.AsSpan(FirstWrittenByte, written));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                buffers.Return(buffer);
            }
        }
    }
}
