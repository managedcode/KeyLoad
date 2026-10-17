using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Publishes exact-pair removal and retains immutable old-lifetime completion history.</summary>
internal static class ResourceRetirementFinalizationStorage
{
    private const string CompletedIncarnation = "The resource incarnation was already finalized.";

    internal static ResourceRetirementFinalizationV1? ReadHistory(IKeyValueView view,
        ResourceLifetimeV1 lifetime, DatabaseLimits limits)
    {
        var completed = Read(view, ResourceRetirementFinalizationKeys.History(lifetime), limits);
        if (completed is not null && !ResourceRetirementValidation.Same(completed.Control.Lifetime, lifetime))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        return completed;
    }

    internal static ResourceRetirementFinalizationV1? ReadLatest(IKeyValueView view,
        string tenantId, string databaseId, string resourceName, DatabaseLimits limits)
    {
        var completed = Read(view, ResourceRetirementFinalizationKeys.Latest(tenantId, databaseId, resourceName), limits);
        if (completed is null)
        {
            return null;
        }
        var lifetime = completed.Control.Lifetime;
        if (lifetime.TenantId != tenantId || lifetime.DatabaseId != databaseId || lifetime.ResourceName != resourceName)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var historical = ReadHistory(view, lifetime, limits);
        if (historical is null || !ResourceRetirementValidation.Same(historical, completed))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        return completed;
    }

    internal static void RequireCreation(IKeyValueView view, ResourceLifetimeV1 proposed, DatabaseLimits limits)
    {
        _ = ReadLatest(view, proposed.TenantId, proposed.DatabaseId, proposed.ResourceName, limits);
        if (ReadHistory(view, proposed, limits) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, CompletedIncarnation);
        }
    }

    internal static void Finalize(IAtomicTransaction transaction,
        ResourceRetirementFinalizationV1 completed, DatabaseLimits limits)
    {
        ResourceRetirementFinalizationValidation.Require(completed, limits);
        var control = completed.Control;
        var lifetime = control.Lifetime;
        var previous = ReadHistory(transaction, lifetime, limits);
        if (previous is not null)
        {
            if (!ResourceRetirementValidation.Same(previous, completed))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return;
        }
        _ = ReadLatest(transaction, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName, limits);
        RequireCurrentPair(transaction, control, limits);
        transaction.Delete(KeySpace.Resource(lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName));
        transaction.Delete(ResourceLifetimeCatalog.Key(lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName));
        transaction.PutRecord(ResourceRetirementFinalizationKeys.History(lifetime), completed);
        transaction.PutRecord(ResourceRetirementFinalizationKeys.Latest(
            lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName), completed);
    }

    private static void RequireCurrentPair(IKeyValueView view, ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        var expected = control.Lifetime;
        var definition = ResourceRetirementStorage.Read<ResourceDefinition>(view,
            KeySpace.Resource(expected.TenantId, expected.DatabaseId, expected.ResourceName), limits);
        var lifetime = ResourceRetirementStorage.Read<ResourceLifetimeV1>(view,
            ResourceLifetimeCatalog.Key(expected.TenantId, expected.DatabaseId, expected.ResourceName), limits);
        if (definition is null && lifetime is null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        if (definition is null || lifetime is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceLifetimeIdentity.Validate(lifetime, expected.TenantId, expected.DatabaseId, definition);
        if (!ResourceRetirementValidation.Same(lifetime, expected)
            || !ResourceRetirementValidation.Same(definition, control.Definition))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
    }

    private static ResourceRetirementFinalizationV1? Read(IKeyValueView view, byte[] key, DatabaseLimits limits)
    {
        var completed = ResourceRetirementStorage.Read<ResourceRetirementFinalizationV1>(view, key, limits);
        if (completed is not null)
        {
            ResourceRetirementFinalizationValidation.Require(completed, limits);
        }
        return completed;
    }
}
