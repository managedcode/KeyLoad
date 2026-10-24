using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ResourceLifetimeReadBinding operations against the caller's database context.</summary>
public static class ResourceLifetimeReadBinding
{
    /// <summary>Resolves and binds a resource after the caller's fresh coarse authorization in this same cut.</summary>
    internal static ResourceDefinition BindAuthorizedReadResource(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var pair = ResourceDefinitionBinding.ReadBoundResourcePair(view, partition, name, kind);
        BindAuthenticatedReadResourceLifetime(view, partition, pair.Definition, pair.Lifetime);
        return pair.Definition;
    }

    /// <summary>Binds an already authenticated source pair without substituting the target's local catalog.</summary>
    internal static void BindAuthenticatedReadResourceLifetime(IKeyValueView view, PartitionRef partition, ResourceDefinition definition, ResourceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lifetime);
        EngineResourceAccess.ValidatePartition(partition);
        ResourceLifetimeIdentity.Validate(lifetime, partition.TenantId, partition.DatabaseId, definition);
        if (partition.TransactionDomainId != lifetime.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }

        if (view is IResourceLifetimeReadBindingView binding)
        {
            binding.BindResourceLifetime(partition, definition, lifetime);
        }
    }
}
