using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementPreparationCounters
{
    internal static void RequireZero(IKeyValueView view, byte[] key, DatabaseLimits limits)
    {
        if (Read(view, key, limits) != ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
        }
    }

    internal static void Add(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        Change(transaction, ResourceRetirementPreparationKeys.Global(), ResourceRetirementProtocol.First, limits);
        Change(transaction, ResourceRetirementPreparationKeys.Database(lifetime.TenantId, lifetime.DatabaseId),
            ResourceRetirementProtocol.First, limits);
    }

    internal static void Remove(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        Change(transaction, ResourceRetirementPreparationKeys.Global(), -ResourceRetirementProtocol.First, limits);
        Change(transaction, ResourceRetirementPreparationKeys.Database(lifetime.TenantId, lifetime.DatabaseId),
            -ResourceRetirementProtocol.First, limits);
    }

    internal static long Read(IKeyValueView view, byte[] key, DatabaseLimits limits)
    {
        long count = ResourceRetirementProtocol.Empty;
        view.ReadValue(key, value =>
        {
            if (value.Length > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            count = NativeSerialization.Deserialize<long>(value);
            if (count <= ResourceRetirementProtocol.Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        });
        return count;
    }

    private static void Change(IAtomicTransaction transaction, byte[] key, int delta, DatabaseLimits limits)
    {
        var count = Read(transaction, key, limits);
        if (delta > ResourceRetirementProtocol.Empty && count >= limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementProtocol.Capacity);
        }
        var next = count + delta;
        if (next < ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        if (next == ResourceRetirementProtocol.Empty)
        {
            transaction.Delete(key);
        }
        else
        {
            transaction.Put(key, NativeSerialization.Serialize(next));
        }
    }
}
