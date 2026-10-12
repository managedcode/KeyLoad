using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphPhaseCommandIdentity
{
    private const int GuidBytes = 16;
    internal static Guid Create(RemoteGraphIntentV1 intent, OperationKind kind,
        PartitionHost partition, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (kind is not (OperationKind.RemoteGraphReceive or OperationKind.RemoteGraphComplete))
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphNativeProtocol.InvalidProof); }
        var bytes = RemoteGraphNativeResultEncoding.Encode(new Identity(intent, kind),
            partition.Database.Limits.MaxBatchBytes, work);
        try
        {
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(bytes.Span, hash);
            return new Guid(hash[..GuidBytes]);
        }
        finally { CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(bytes).Span); }
    }

    [global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphNativeProtocol.PhaseIdentityAlias)]
    internal sealed record Identity([property: global::Orleans.Id(0)] RemoteGraphIntentV1 Intent,
        [property: global::Orleans.Id(1)] OperationKind Kind);
}
