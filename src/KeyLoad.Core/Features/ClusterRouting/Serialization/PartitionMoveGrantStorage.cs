using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionMoveGrantStorage
{
    internal static byte[] MoveCountKey(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(PartitionMoveProtocol.MoveGrantCountSpace, partition, moveId);

    internal static byte[] MovePrefix(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(PartitionMoveProtocol.GrantMoveIndexSpace, partition, moveId);

    internal static byte[] MoveKey(PartitionRef partition, Guid moveId, Guid grantId)
        => KeySpace.Partition(PartitionMoveProtocol.GrantMoveIndexSpace, partition, moveId, grantId);

    internal static byte[] Prefix(PartitionRef partition)
        => KeySpace.Partition(PartitionMoveProtocol.GrantSpace, partition);

    internal static byte[] Key(PartitionRef partition, Guid grantId)
        => KeyCodec.Encode(PartitionMoveProtocol.GrantSpace, partition.TenantId, partition.DatabaseId,
            partition.TransactionDomainId, partition.PartitionKey, grantId);

    internal static byte[] PrincipalKey(string principalId)
        => KeyCodec.Encode(PartitionMoveProtocol.PrincipalGrantSpace, principalId);

    internal static PartitionMovePhaseGrant? Read(IKeyValueView view, PartitionRef partition,
        Guid grantId, int maximumBytes)
        => PartitionMoveTargetStorage.Read<PartitionMovePhaseGrant>(view, Key(partition, grantId), maximumBytes);

    internal static void Write(IAtomicTransaction transaction, PartitionMovePhaseGrant grant, int maximumBytes)
    {
        PartitionMoveTargetStorage.Write(transaction, Key(grant.Partition, grant.GrantId), grant, maximumBytes);
        transaction.Put(MoveKey(grant.Partition, grant.MoveId, grant.GrantId), NativeSerialization.Serialize(grant.GrantId));
    }

    internal static byte[] DatabaseKey(string tenantId, string databaseId)
        => KeyCodec.Encode(PartitionMoveProtocol.DatabaseGrantSpace, tenantId, databaseId);

    internal static long Outstanding(IKeyValueView view, string principalId)
        => Outstanding(view, PrincipalKey(principalId));

    internal static long Outstanding(IKeyValueView view, byte[] key)
    {
        long count = PartitionMoveProtocol.EmptyCount;
        view.ReadValue(key, bytes => count = NativeSerialization.Deserialize<long>(bytes));
        if (count < PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority); }
        return count;
    }

    internal static void ChangeOutstanding(IAtomicTransaction transaction, string principalId,
        bool admit, int maximumCount)
    {
        ChangeOutstanding(transaction, PrincipalKey(principalId), admit, maximumCount);
    }

    internal static void ChangeOutstanding(IAtomicTransaction transaction, byte[] key,
        bool admit, int maximumCount)
    {
        var count = Outstanding(transaction, key);
        if (admit && count >= maximumCount || !admit && count == PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(admit ? ErrorCode.BudgetExceeded : ErrorCode.Corruption, PartitionMoveProtocol.Capacity); }
        transaction.Put(key, NativeSerialization.Serialize(admit
            ? count + PartitionMoveProtocol.SequenceStep : count - PartitionMoveProtocol.SequenceStep));
    }
}
