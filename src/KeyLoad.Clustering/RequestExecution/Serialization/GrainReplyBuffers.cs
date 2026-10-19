using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Clustering;

/// <summary>Releases owned reply content after failed publication.</summary>
internal static class GrainReplyBuffers
{
    internal static void Clear(GrainOperationReply reply)
    {
        if (MemoryMarshal.TryGetArray(reply.Payload, out var bytes) && bytes.Array is not null)
        {
            CryptographicOperations.ZeroMemory(bytes.AsSpan());
        }
    }
}
