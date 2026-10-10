using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairIdentity
{
    internal static Guid CommandId(RemoteTransferCoordinationHint hint, QueueTransferRepairStage stage)
    {
        RequireGenerations(hint.AcceptGeneration, hint.AcceptPolicyGeneration, hint.CompleteGeneration);
        if (stage == QueueTransferRepairStage.Accept)
        {
            return hint.AcceptPolicyGeneration == RemoteTransferRepairProtocol.InitialGeneration
                ? RemoteTransferAttemptIdentity.AcceptId(hint, hint.AcceptGeneration)
                : Hash(hint, RemoteTransferRepairProtocol.AcceptDomain, hint.AcceptGeneration, hint.AcceptPolicyGeneration);
        }
        if (stage != QueueTransferRepairStage.Complete)
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
        return hint.CompleteGeneration == RemoteTransferRepairProtocol.InitialGeneration
            ? RemoteTransferCoordinationIdentity.CommandId(hint, RemoteTransferCoordinationProtocol.CompleteStage)
            : Hash(hint, RemoteTransferRepairProtocol.CompleteDomain, hint.CompleteGeneration);
    }

    internal static Guid AdvanceId(RemoteTransferCoordinationHint hint, QueueTransferRepairStage stage, string outcomeDigest)
    {
        RequireGenerations(hint.AcceptGeneration, hint.AcceptPolicyGeneration, hint.CompleteGeneration);
        if (!Enum.IsDefined(stage) || !RemoteTransferDependencyShape.Digest(outcomeDigest))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
        return Hash(hint, RemoteTransferRepairProtocol.AdvanceDomain, (int)stage, hint.AcceptGeneration,
            hint.AcceptPolicyGeneration, hint.CompleteGeneration, outcomeDigest);
    }

    private static Guid Hash(RemoteTransferCoordinationHint hint, string domain, params object?[] suffix)
    {
        var source = hint.Source.Partition;
        var target = hint.Destination.Partition;
        object?[] identity = [domain, source.TenantId, source.DatabaseId, source.TransactionDomainId,
            source.PartitionKey, hint.Source.Queue, target.TenantId, target.DatabaseId, target.TransactionDomainId,
            target.PartitionKey, hint.Destination.Queue, hint.TransferId, hint.PrincipalId, hint.Fingerprint,
            hint.IntentDigest, hint.SourceCut.Incarnation, .. suffix];
        return new Guid(SHA256.HashData(KeyCodec.Encode(identity)).AsSpan(
            RemoteTransferAttemptProtocol.FirstIndex, RemoteTransferAttemptProtocol.GuidBytes));
    }

    internal static void RequireGenerations(long capacity, long policy, long complete)
    {
        var first = RemoteTransferRepairProtocol.InitialGeneration;
        if (capacity < first || policy < first || complete < first)
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
    }
}
