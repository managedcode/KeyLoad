using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Resolves and binds a resource after the caller's fresh coarse authorization in this same cut.</summary>
    internal ResourceDefinition BindAuthorizedReadResource(IKeyValueView view, PartitionRef partition,
        string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var pair = ReadBoundResourcePair(view, partition, name, kind);
        BindAuthenticatedReadResourceLifetime(view, partition, pair.Definition, pair.Lifetime);
        return pair.Definition;
    }

    /// <summary>Binds an already authenticated source pair without substituting the target's local catalog.</summary>
    internal void BindAuthenticatedReadResourceLifetime(IKeyValueView view, PartitionRef partition,
        ResourceDefinition definition, ResourceLifetimeV1 lifetime)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lifetime);
        ValidatePartition(partition);
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
