using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed class ResourceEffectBindingScope(IOptions<DatabaseLimits> options)
{
    private readonly DatabaseLimits limits = options.Value;
    private readonly HashSet<ResourceLifetimeV1> observed = [];
    private ResourceEffectAdmissionV1? admitted;
    private long retainedBytes;
    private long readBytes;
    private long examined;

    internal void ChargeRead(long bytes)
    {
        if (bytes < ResourceEffectAdmissionProtocol.Zero || bytes > limits.MaxQueryReadBytes - readBytes
            || examined >= limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceEffectAdmissionProtocol.Capacity);
        }
        readBytes += bytes;
        examined++;
    }

    internal void Observe(PartitionRef partition, ResourceLifetimeV1 lifetime)
    {
        if (partition.TenantId != lifetime.TenantId || partition.DatabaseId != lifetime.DatabaseId
            || partition.TransactionDomainId != lifetime.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        ObserveLifetime(lifetime);
    }

    internal void ObserveLifetime(ResourceLifetimeV1 lifetime)
    {
        if (admitted is not null && !admitted.Bindings.Contains(lifetime))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        if (observed.Contains(lifetime))
        {
            return;
        }
        var bytes = NativeSerialization.Measure(lifetime);
        if (bytes <= ResourceEffectAdmissionProtocol.Zero || bytes > limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceEffectAdmissionProtocol.Capacity);
        }
        observed.Add(lifetime);
        retainedBytes += bytes;
    }

    internal ImmutableArray<ResourceLifetimeV1> Capture()
        => observed.OrderBy(value => value.TenantId, StringComparer.Ordinal)
            .ThenBy(value => value.DatabaseId, StringComparer.Ordinal)
            .ThenBy(value => value.ResourceName, StringComparer.Ordinal)
            .ThenBy(value => value.Incarnation.Value, StringComparer.Ordinal).ToImmutableArray();

    // Composition source selection runs at its original business point after the clock gate.
    // Every originally signed lifetime is nevertheless checked in the current cut before that gate.
    internal void ActivateOriginalBindings(IKeyValueView view, ResourceEffectAdmissionV1 value,
        Action<ResourceLifetimeV1> authorizeUnobservedCollection)
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
            var current = ResourceLifetimeCatalog.Read(view, lifetime.TenantId, lifetime.DatabaseId,
                lifetime.ResourceName, definition);
            if (current != lifetime)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
            }
        }
        admitted = value;
    }

    internal void RequireComplete()
    {
        if (admitted is not null && !NativeSerialization.Serialize(Capture()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(admitted.Bindings)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
    }

    internal void Activate(IKeyValueView view, ResourceEffectAdmissionV1 value)
    {
        if (!NativeSerialization.Serialize(Capture()).AsSpan().SequenceEqual(NativeSerialization.Serialize(value.Bindings)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        foreach (var lifetime in value.Bindings)
        {
        }
        admitted = value;
    }
}
