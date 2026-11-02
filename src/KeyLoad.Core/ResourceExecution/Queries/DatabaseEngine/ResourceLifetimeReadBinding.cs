using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ResourceLifetimeReadBinding operations against the caller's database context.</summary>
public static class ResourceLifetimeReadBinding
{
    /// <summary>Resolves and binds a resource after the caller's fresh coarse authorization in this same cut.</summary>
    internal static ResourceDefinition BindAuthorizedReadResource(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
        => BindAuthorizedReadResourcePair(view, partition, name, kind).Definition;

    /// <summary>Retains the authenticated definition and lifetime for reuse within the caller's existing cut.</summary>
    internal static (ResourceDefinition Definition, ResourceLifetime Lifetime) BindAuthorizedReadResourcePair(
        IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var pair = ResourceDefinitionBinding.ReadBoundResourcePair(view, partition, name, kind);
        BindAuthenticatedReadResourceLifetime(view, partition, pair.Definition, pair.Lifetime);
        return pair;
    }

    /// <summary>Binds an already authenticated source pair without substituting the target's local catalog.</summary>
    internal static void BindAuthenticatedReadResourceLifetime(IKeyValueView view, PartitionRef partition, ResourceDefinition definition, ResourceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(definition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(lifetime) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
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
