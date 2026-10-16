using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Stores exact-lifetime durable barriers and constant-time topology blockers in the original transaction.</summary>
internal static class ResourceRetirementStorage
{
    private const string ControlSpace = "resource-retirement-control-v1";
    private const string FenceSpace = "resource-retirement-fence-v1";
    private const string DatabaseBlockers = "resource-retirement-database-blockers-v1";
    private const string GlobalBlockers = "resource-retirement-global-blockers-v1";

    internal static byte[] ControlKey(ResourceLifetimeV1 lifetime)
        => ControlKey(lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation);

    internal static byte[] ControlKey(string tenantId, string databaseId, string resourceName, ResourceIncarnationV1 incarnation)
        => KeyCodec.Encode(ControlSpace, tenantId, databaseId, resourceName, incarnation.Value);

    internal static byte[] FenceKey(ResourceLifetimeV1 lifetime)
        => KeyCodec.Encode(FenceSpace, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, lifetime.Incarnation.Value);

    internal static ResourceRetirementStateV1? ReadControl(IKeyValueView view, ResourceLifetimeV1 lifetime,
        DatabaseLimits limits)
    {
        var state = Read<ResourceRetirementStateV1>(view, ControlKey(lifetime), limits);
        if (state is not null)
        {
            ValidateStored(() => ResourceRetirementStateValidation.State(state, limits));
            ResourceRetirementAttemptStorage.ValidatePointer(view, state, limits);
            RequireLifetime(state.Control.Lifetime, lifetime);
        }
        return state;
    }

    internal static ResourceRetirementOwnerFenceV1? ReadFence(IKeyValueView view, ResourceLifetimeV1 lifetime,
        DatabaseLimits limits)
    {
        var fence = Read<ResourceRetirementOwnerFenceV1>(view, FenceKey(lifetime), limits);
        if (fence is not null)
        {
            ValidateStored(() => ResourceRetirementStateValidation.Fence(fence, limits));
            RequireLifetime(fence.Control.Lifetime, lifetime);
        }
        return fence;
    }

    internal static ResourceRetirementStateV1? ReadControl(IKeyValueView view, ResourceRetirementRequestV1 request,
        DatabaseLimits limits)
    {
        var state = Read<ResourceRetirementStateV1>(view, ControlKey(request.TenantId, request.DatabaseId,
            request.ResourceName, request.ExpectedIncarnation), limits);
        if (state is not null)
        {
            ValidateStored(() => ResourceRetirementStateValidation.State(state, limits));
            ResourceRetirementAttemptStorage.ValidatePointer(view, state, limits);
            var original = state.Control.Request;
            if (original.TenantId != request.TenantId || original.DatabaseId != request.DatabaseId
                || original.ResourceName != request.ResourceName || original.ExpectedIncarnation != request.ExpectedIncarnation)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        return state;
    }

    private static void ValidateStored(Action validate)
    {
        try
        {
            validate();
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Validation or ErrorCode.TokenInvalidated
            or ErrorCode.OwnershipLost or ErrorCode.RecoveryRequired or ErrorCode.BudgetExceeded)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    internal static T? Read<T>(IKeyValueView view, byte[] key, DatabaseLimits limits,
        StorageReadObserver? observer = null) where T : class
    {
        T? result = null;
        view.ReadValue(key, bytes =>
        {
            if (bytes.Length > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            result = NativeSerialization.Deserialize<T>(bytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }, observer);
        return result;
    }

    internal static void RequireOpen(IKeyValueView view, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        if (ReadControl(view, lifetime, limits) is not null || ReadFence(view, lifetime, limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
        }
    }

    internal static void RequireNoRetirement(IKeyValueView view, string tenantId, string databaseId, DatabaseLimits limits)
        => RequireZero(view, KeyCodec.Encode(DatabaseBlockers, tenantId, databaseId), limits);

    internal static void RequireNoRetirement(IKeyValueView view, DatabaseLimits limits)
        => RequireZero(view, KeyCodec.Encode(GlobalBlockers), limits);

    internal static void ReleaseBlocker(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        Decrement(transaction, KeyCodec.Encode(DatabaseBlockers, lifetime.TenantId, lifetime.DatabaseId), limits);
        Decrement(transaction, KeyCodec.Encode(GlobalBlockers), limits);
    }

    internal static void CreateBlocker(IAtomicTransaction transaction, ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        Increment(transaction, KeyCodec.Encode(DatabaseBlockers, lifetime.TenantId, lifetime.DatabaseId), limits);
        Increment(transaction, KeyCodec.Encode(GlobalBlockers), limits);
    }

    internal static void WriteState(IAtomicTransaction transaction, ResourceRetirementStateV1 state, DatabaseLimits limits)
    {
        ResourceRetirementStateValidation.State(state, limits);
        transaction.PutRecord(ControlKey(state.Control.Lifetime), state);
    }

    internal static void WriteFence(IAtomicTransaction transaction, ResourceRetirementOwnerFenceV1 fence, DatabaseLimits limits)
    {
        ResourceRetirementStateValidation.Fence(fence, limits);
        transaction.PutRecord(FenceKey(fence.Control.Lifetime), fence);
    }

    private static void RequireLifetime(ResourceLifetimeV1 actual, ResourceLifetimeV1 expected)
    {
        if (!ResourceRetirementValidation.Same(actual, expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    private static long Count(IKeyValueView view, byte[] key, DatabaseLimits limits)
    {
        long result = ResourceRetirementProtocol.Empty;
        view.ReadValue(key, bytes =>
        {
            if (bytes.Length > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            result = NativeSerialization.Deserialize<long>(bytes);
            if (result <= ResourceRetirementProtocol.Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        });
        return result;
    }

    private static void RequireZero(IKeyValueView view, byte[] key, DatabaseLimits limits)
    {
        if (Count(view, key, limits) != ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Retiring);
        }
    }

    private static void Decrement(IAtomicTransaction transaction, byte[] key, DatabaseLimits limits)
    {
        var count = Count(transaction, key, limits);
        if (count <= ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        if (count == ResourceRetirementProtocol.First)
        {
            transaction.Delete(key);
        }
        else
        {
            transaction.PutRecord(key, count - ResourceRetirementProtocol.First);
        }
    }

    private static void Increment(IAtomicTransaction transaction, byte[] key, DatabaseLimits limits)
    {
        var count = Count(transaction, key, limits);
        if (count == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementProtocol.Capacity);
        }
        transaction.Put(key, NativeSerialization.Serialize(count + ResourceRetirementProtocol.First));
    }
}
