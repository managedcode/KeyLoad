using System.Security.Cryptography;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Clustering;

/// <summary>One stable command identity per due occurrence, so a repeated sweep or deadline retry replays the persisted outcome.</summary>
internal static class DueDispatchCommandIdentity
{
    private const string DueDomain = "due-dispatch";
    private const string DeadlineDomain = "queue-deadline-dispatch";
    private const int FirstIndex = 0;
    private const int GuidBytes = 16;

    internal static Guid ForDue(DueWorkHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        var partition = hint.Lane.Partition;
        var digest = SHA256.HashData(KeyCodec.Encode(DueDomain, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, hint.Lane.Queue, (int)hint.Kind, hint.Id,
            hint.Revision, hint.Generation, hint.Ordinal));
        return new Guid(digest.AsSpan(FirstIndex, GuidBytes));
    }

    internal static Guid ForDeadline(QueueDeadlineHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        var partition = hint.Lane.Partition;
        var mutation = hint.Mutation;
        var digest = SHA256.HashData(KeyCodec.Encode(DeadlineDomain, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, hint.Lane.Queue, mutation.MessageId,
            (int)mutation.ExpectedState, mutation.ExpectedStateVersion, mutation.ExpectedLeaseVersion,
            mutation.ExpectedDeadline, (int)mutation.Kind));
        return new Guid(digest.AsSpan(FirstIndex, GuidBytes));
    }
}
