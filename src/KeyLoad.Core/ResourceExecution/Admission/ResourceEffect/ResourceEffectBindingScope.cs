using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.ResourceExecution;

internal sealed class ResourceEffectBindingScope
{
    private readonly HashSet<ResourceLifetime> observed = [];
    private ResourceEffectAdmission? admitted;

    internal void Observe(PartitionRef partition, ResourceLifetime lifetime)
    {
        if (partition.TenantId != lifetime.TenantId || partition.DatabaseId != lifetime.DatabaseId
            || partition.TransactionDomainId != lifetime.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        ObserveLifetime(lifetime);
    }

    internal void ObserveLifetime(ResourceLifetime lifetime)
    {
        if (admitted is not null && !admitted.Bindings.Contains(lifetime))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        if (observed.Contains(lifetime))
        {
            return;
        }
        observed.Add(lifetime);
    }

    internal ImmutableArray<ResourceLifetime> Capture()
        => observed.OrderBy(value => value.TenantId, StringComparer.Ordinal)
            .ThenBy(value => value.DatabaseId, StringComparer.Ordinal)
            .ThenBy(value => value.ResourceName, StringComparer.Ordinal)
            .ThenBy(value => value.Incarnation.Value, StringComparer.Ordinal).ToImmutableArray();

    // Composition source selection runs at its original business point after the clock gate.
    // Every originally admitted lifetime is nevertheless checked in the current cut before that gate.
    internal void ActivateOriginalBindings(IKeyValueView view, ResourceEffectAdmission value,
        Action<ResourceLifetime> authorizeUnobservedCollection)
    {
        ArgumentNullException.ThrowIfNull(authorizeUnobservedCollection);
        if (!observed.IsSubsetOf(value.Bindings))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        // Current static authorization has already resolved its resources. Dynamic endpoint
        // collections need their own current read grant before any private pair/fence diagnostic.
        foreach (var lifetime in value.Bindings)
        {
            if (lifetime.Kind == ResourceKind.Collection && !observed.Contains(lifetime))
            {
                authorizeUnobservedCollection(lifetime);
            }
        }
        foreach (var lifetime in value.Bindings)
        {
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(
                lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName));
            var current = (ResourceLifetimeCatalog.Read(view, lifetime.TenantId, lifetime.DatabaseId,
                lifetime.ResourceName, definition)).Value;
            if (current != lifetime)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
            }
        }
        admitted = value;
    }

    internal void RequireComplete()
    {
        if (admitted is not null && !OrleansSerialization.Serialize(Capture()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(admitted.Bindings)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
    }

    internal void Activate(ResourceEffectAdmission value)
    {
        if (!OrleansSerialization.Serialize(Capture()).AsSpan().SequenceEqual(OrleansSerialization.Serialize(value.Bindings)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        admitted = value;
    }
}
