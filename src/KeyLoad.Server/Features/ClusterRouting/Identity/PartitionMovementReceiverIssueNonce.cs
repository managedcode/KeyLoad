using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ReceiverIssueNonceAlias)]
internal sealed record PartitionMovementReceiverIssueNonce(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: global::Orleans.Id(2)] Guid SourceQueryNonce)
{
    private const int IdentityBytes = 16;

    internal static Guid For(Guid originalPhaseCommandId, Guid sourceQueryNonce)
    {
        if (originalPhaseCommandId == Guid.Empty || sourceQueryNonce == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(NativeSerialization.Serialize(new PartitionMovementReceiverIssueNonce(
            PartitionMoveProtocol.Version, originalPhaseCommandId, sourceQueryNonce)), digest);
        return new Guid(digest[..IdentityBytes]);
    }
}
