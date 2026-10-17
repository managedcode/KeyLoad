using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Restricts continuation keys to the original lifetime's canonical full-partition index.</summary>
internal static class ResourceOwnerInventoryCursorValidation
{
    private const int PartitionFields = 4;
    private const int TenantOffset = 0;
    private const int DatabaseOffset = 1;
    private const int DomainOffset = 2;
    private const int PartitionOffset = 3;

    internal static void Require(ResourceOwnerInventoryCursorV1 cursor, ResourceOwnerInventoryControlV1 control,
        PrincipalRecord principal, DatabaseLimits limits)
    {
        if (cursor is null || cursor.Cut is null || cursor.Cut.ObservedOwner is null
            || cursor.AfterKey.IsEmpty || cursor.AfterKey.Length > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadValidation.Invalid);
        }
        ResourceOwnerInventoryReadValidation.Bound(cursor, limits);
        ResourceOwnerInventoryReadValidation.OwnerCut(cursor.Cut, limits);
        Key(cursor.AfterKey.Span, control.Lifetime);
        if (cursor.InventoryId != control.Request.InventoryId || cursor.Incarnation != control.Lifetime.Incarnation
            || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch
            || cursor.ExpiresAt != control.Request.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryReadValidation.Changed);
        }
    }

    internal static void Key(ReadOnlySpan<byte> key, ResourceLifetimeV1 lifetime)
    {
        try
        {
            RequireKey(key, lifetime);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadValidation.Invalid);
        }
    }

    private static void RequireKey(ReadOnlySpan<byte> key, ResourceLifetimeV1 lifetime)
    {
        var prefix = ResourceOwnerInventoryCatalog.Prefix(lifetime);
        var prefixParts = KeyCodec.Decode(prefix);
        var parts = KeyCodec.Decode(key);
        var offset = prefixParts.Length;
        if (!key.StartsWith(prefix) || parts.Length != offset + PartitionFields
            || !KeyCodec.Encode(parts).AsSpan().SequenceEqual(key)
            || parts[offset + TenantOffset] is not string tenant
            || parts[offset + DatabaseOffset] is not string database
            || parts[offset + DomainOffset] is not string domain
            || parts[offset + PartitionOffset] is not string partitionKey
            || tenant != lifetime.TenantId || database != lifetime.DatabaseId || domain != lifetime.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceOwnerInventoryReadValidation.Invalid);
        }
        DatabaseEngine.ValidatePartition(new(tenant, database, domain, partitionKey));
    }
}
