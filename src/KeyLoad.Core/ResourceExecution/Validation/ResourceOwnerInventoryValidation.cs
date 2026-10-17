namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryValidation
{
    internal static void Origin(ResourceOwnerInventoryOriginV1 origin, bool requireProven)
    {
        if (origin is null || origin.Version != ResourceOwnerInventoryProtocol.Version
            || origin.Revision < ResourceOwnerInventoryProtocol.Zero
            || (origin.Proven && (origin.StoreIncarnation == Guid.Empty
                || origin.Revision == ResourceOwnerInventoryProtocol.Zero)))
        {
            throw Invalid();
        }
        if (requireProven && !origin.Proven)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown);
        }
    }

    internal static void Lifetime(ResourceLifetimeV1 lifetime)
    {
        if (lifetime is null)
        {
            throw Invalid();
        }
        // This validates serialized identity only; producer authority is catalog or authenticated coverage.
        ResourceLifetimeIdentity.Validate(lifetime, lifetime.TenantId, lifetime.DatabaseId,
            new ResourceDefinition(lifetime.ResourceName, lifetime.Kind, lifetime.TransactionDomainId));
    }

    internal static void Enrollment(ResourceOwnerInventoryEnrollmentV1 entry, bool requireProven)
    {
        if (entry is null || entry.Version != ResourceOwnerInventoryProtocol.Version || entry.Partition is null)
        {
            throw Invalid();
        }
        Lifetime(entry.Lifetime);
        Origin(entry.Origin, requireProven);
        try
        {
            DatabaseEngine.ValidatePartition(entry.Partition);
        }
        catch (KeyLoadException)
        {
            throw Invalid();
        }
        if (entry.Partition.TenantId != entry.Lifetime.TenantId
            || entry.Partition.DatabaseId != entry.Lifetime.DatabaseId
            || entry.Partition.TransactionDomainId != entry.Lifetime.TransactionDomainId)
        {
            throw Invalid();
        }
    }

    internal static T Decode<T>(ReadOnlySpan<byte> value, DatabaseLimits limits)
    {
        if (value.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryProtocol.Capacity);
        }
        return NativeSerialization.Deserialize<T>(value);
    }

    internal static byte[] Encode<T>(T value, DatabaseLimits limits)
    {
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
        }
        return bytes;
    }

    internal static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryProtocol.Invalid);
}
