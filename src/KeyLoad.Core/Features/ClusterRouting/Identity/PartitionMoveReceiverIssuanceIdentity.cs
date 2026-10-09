using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMoveReceiverIssuanceIdentity.SerializerAlias)]
internal sealed record PartitionMoveReceiverIssuanceIdentity(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid OriginalPhaseCommandId)
{
    internal const string SerializerAlias = "keyload.server.partition-move-receiver-issue-identity.v1";
    private const int IdentityBytes = 16;

    internal static Guid For(Guid originalPhaseCommandId)
    {
        if (originalPhaseCommandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(NativeSerialization.Serialize(new PartitionMoveReceiverIssuanceIdentity(
            PartitionMoveProtocol.Version, originalPhaseCommandId)), digest);
        return new Guid(digest[..IdentityBytes]);
    }
}
