using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Owns the adjacent typed lifetime metadata inside the original catalog transaction.</summary>
internal static class ResourceLifetimeCatalog
{
    private const string LifetimeSpace = "resource-lifetime-v1";
    private const string InvalidPair = "The resource definition and lifetime metadata do not form a valid pair.";
    private const string AlreadyCreated = "The resource already has a persisted lifetime.";

    internal static byte[] Prefix() => KeyCodec.Encode(LifetimeSpace);

    internal static byte[] Key(string tenantId, string databaseId, string resourceName)
        => KeyCodec.Encode(LifetimeSpace, tenantId, databaseId, resourceName);

    /// <summary>Reads a validated pair; only the complete absence of both records is an absent resource.</summary>
    internal static ResourceLifetimeV1? Read(IKeyValueView view, string tenantId, string databaseId,
        string resourceName, ResourceDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(view);
        var lifetime = view.GetRecord<ResourceLifetimeV1>(Key(tenantId, databaseId, resourceName));
        if (definition is null && lifetime is null)
        {
            return null;
        }
        if (definition is null || lifetime is null
            || !string.Equals(definition.Name, resourceName, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidPair);
        }
        ResourceLifetimeIdentity.Validate(lifetime, tenantId, databaseId, definition);
        return lifetime;
    }

    /// <summary>Stages a new server-derived lifetime; the caller stages its definition in this same transaction.</summary>
    internal static ResourceLifetimeV1 Create(IAtomicTransaction transaction, ReplicatedOperation original,
        string tenantId, string databaseId, ResourceDefinition definition, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(definition);
        var previous = transaction.GetRecord<ResourceDefinition>(KeySpace.Resource(tenantId, databaseId, definition.Name));
        if (Read(transaction, tenantId, databaseId, definition.Name, previous) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, AlreadyCreated);
        }
        var created = ResourceLifetimeIdentity.Create(original, tenantId, databaseId, definition);
        ResourceRetirementFinalizationStorage.RequireCreation(transaction, created, limits);
        transaction.PutRecord(Key(tenantId, databaseId, definition.Name), created);
        ResourceOwnerInventoryCreation.Record(transaction, created);
        return created;
    }
}
