using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Counts original owner-purpose prepared rows without deleting their retained recovery evidence.</summary>
internal static class ResourceRetirementPreparedReplacementAccounting
{
    private const string GlobalSpace = "resource-retirement-prepared-replacement-blockers-v1";
    private const string LifetimeSpace = "resource-retirement-prepared-replacement-life-v1";
    private const string ReplacementBlocked = "A durable resource retirement operation prevents storage replacement.";

    private static byte[] LifeKey(ResourceLifetimeV1 lifetime, PhysicalShardRecord owner)
        => KeyCodec.Encode(LifetimeSpace, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName,
            lifetime.Incarnation.Value, owner.PhysicalShardId, owner.Incarnation);

    internal static long ReadLife(IKeyValueView view, ResourceLifetimeV1 lifetime, PhysicalShardRecord owner,
        DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        var count = ReadCount(view, LifeKey(lifetime, owner), limits, observer);
        if (count <= ResourceRetirementProtocol.Empty)
        {
            throw Invalid();
        }
        return count;
    }

    internal static void RequireRow(IKeyValueView view, ResourceRetirementAttemptContextV1 context,
        DatabaseLimits limits, StorageReadObserver? observer)
    {
        var lifetime = ResourceRetirementAttemptContextValidation.Lifetime(context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
        var count = ReadLife(view, lifetime, owner, limits, observer);
        var total = ReadCount(view, KeyCodec.Encode(GlobalSpace), limits, observer);
        var released = ResourceRetirementConservationReleaseStorage.Read(view, lifetime, owner, limits, observer);
        if (released is not null)
        {
            if (released.OwnerRelease.PreparedReplacementCount != count)
            {
                throw Invalid();
            }
        }
        else if (total < count)
        {
            throw Invalid();
        }
    }

    internal static void RecordNew(IAtomicTransaction transaction, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, DatabaseLimits limits)
    {
        var lifetime = ResourceRetirementAttemptContextValidation.Lifetime(context);
        var owner = ResourceRetirementAttemptContextValidation.ExecutionOwner(context);
        if (ResourceRetirementConservationReleaseStorage.Read(transaction, lifetime, owner, limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
        }
        if (context.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation)
        {
            return;
        }

        var key = LifeKey(lifetime, owner);
        var count = ReadCount(transaction, key, limits);
        var total = ReadCount(transaction, KeyCodec.Encode(GlobalSpace), limits);
        if (count > total || count == ResourceRetirementProtocol.Empty
            && attempt.Ordinal > ResourceRetirementProtocol.First)
        {
            throw Invalid();
        }

        if (count == long.MaxValue || total == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementProtocol.Capacity);
        }
        transaction.PutRecord(key, count + ResourceRetirementProtocol.First);
        transaction.PutRecord(KeyCodec.Encode(GlobalSpace), total + ResourceRetirementProtocol.First);
    }

    internal static void Release(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime,
        PhysicalShardRecord owner, DatabaseLimits limits)
    {
        var count = ReadLife(transaction, lifetime, owner, limits);
        var released = ResourceRetirementConservationReleaseStorage.Read(transaction, lifetime, owner, limits)
            ?? throw Invalid();
        var total = ReadCount(transaction, KeyCodec.Encode(GlobalSpace), limits);
        if (released.OwnerRelease.PreparedReplacementCount != count || total < count)
        {
            throw Invalid();
        }

        if (total == count)
        {
            transaction.Delete(KeyCodec.Encode(GlobalSpace));
        }
        else
        {
            transaction.PutRecord(KeyCodec.Encode(GlobalSpace), total - count);
        }
    }

    internal static void RequireNoReplacement(IKeyValueView view, DatabaseLimits limits)
    {
        if (ReadCount(view, KeyCodec.Encode(GlobalSpace), limits) != ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplacementBlocked);
        }
    }

    private static long ReadCount(IKeyValueView view, byte[] key, DatabaseLimits limits,
        StorageReadObserver? observer = null)
    {
        long count = ResourceRetirementProtocol.Empty;
        view.ReadValue(key, bytes =>
        {
            if (bytes.Length > limits.MaxBatchBytes)
            {
                throw Invalid();
            }

            count = NativeSerialization.Deserialize<long>(bytes);
            if (count <= ResourceRetirementProtocol.Empty)
            {
                throw Invalid();
            }
        }, observer);
        return count;
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
