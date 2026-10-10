using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAttemptIdentity
{
    internal static Guid AcceptId(RemoteTransferCoordinationHint hint, long generation)
        => AcceptId(hint.Source, hint.Destination, hint.TransferId, hint.PrincipalId,
            hint.Fingerprint, hint.IntentDigest, hint.SourceCut.Incarnation, generation);

    internal static Guid AcceptId(QueueLaneRef source, QueueLaneRef destination, Guid transferId,
        string principal, string fingerprint, string intentDigest, Guid incarnation, long generation)
    {
        RequireGeneration(generation);
        var domain = generation == RemoteTransferAttemptProtocol.FirstGeneration
            ? RemoteTransferCoordinationProtocol.CommandDomain : RemoteTransferAttemptProtocol.AttemptDomain;
        object?[] components = [domain, RemoteTransferCoordinationProtocol.AcceptStage,
            source.Partition.TenantId, source.Partition.DatabaseId, source.Partition.TransactionDomainId,
            source.Partition.PartitionKey, source.Queue, destination.Partition.TenantId,
            destination.Partition.DatabaseId, destination.Partition.TransactionDomainId,
            destination.Partition.PartitionKey, destination.Queue, transferId, principal, fingerprint, intentDigest, incarnation];
        return Hash(KeyCodec.Encode(generation == RemoteTransferAttemptProtocol.FirstGeneration
            ? components : [.. components, generation]));
    }

    internal static Guid AdvanceId(RemoteTransferCoordinationHint hint, long generation, string digest)
    {
        RequireGeneration(generation);
        var source = hint.Source.Partition;
        var destination = hint.Destination.Partition;
        return Hash(KeyCodec.Encode(RemoteTransferAttemptProtocol.AdvanceDomain, source.TenantId,
            source.DatabaseId, source.TransactionDomainId, source.PartitionKey, hint.Source.Queue,
            destination.TenantId, destination.DatabaseId, destination.TransactionDomainId,
            destination.PartitionKey, hint.Destination.Queue, hint.TransferId, hint.PrincipalId,
            hint.Fingerprint, hint.IntentDigest, hint.SourceCut.Incarnation, generation, digest));
    }

    private static void RequireGeneration(long generation)
    {
        if (generation < RemoteTransferAttemptProtocol.FirstGeneration)
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferAttemptProtocol.Invalid); }
    }

    private static Guid Hash(byte[] bytes)
        => new(SHA256.HashData(bytes).AsSpan(RemoteTransferAttemptProtocol.FirstIndex, RemoteTransferAttemptProtocol.GuidBytes));
}
