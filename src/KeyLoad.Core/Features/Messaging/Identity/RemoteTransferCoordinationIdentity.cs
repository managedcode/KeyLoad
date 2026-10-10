using System.Security.Cryptography;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationIdentity
{
    internal static string IntentDigest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static Guid CommandId(RemoteTransferCoordinationHint hint, string stage)
    {
        if (stage is not (RemoteTransferCoordinationProtocol.AcceptStage or RemoteTransferCoordinationProtocol.CompleteStage))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferCoordinationProtocol.InvalidHint); }
        var source = hint.Source.Partition;
        var target = hint.Destination.Partition;
        var digest = SHA256.HashData(KeyCodec.Encode(RemoteTransferCoordinationProtocol.CommandDomain, stage,
            source.TenantId, source.DatabaseId, source.TransactionDomainId, source.PartitionKey, hint.Source.Queue,
            target.TenantId, target.DatabaseId, target.TransactionDomainId, target.PartitionKey, hint.Destination.Queue,
            hint.TransferId, hint.PrincipalId, hint.Fingerprint, hint.IntentDigest, hint.SourceCut.Incarnation));
        return new Guid(digest.AsSpan(RemoteTransferCoordinationProtocol.FirstIndex, RemoteTransferCoordinationProtocol.GuidBytes));
    }
}
