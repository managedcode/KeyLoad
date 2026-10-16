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

    internal void Activate(IKeyValueView view, ResourceEffectAdmissionV1 value)
    {
        if (!NativeSerialization.Serialize(Capture()).AsSpan().SequenceEqual(NativeSerialization.Serialize(value.Bindings)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceEffectAdmissionProtocol.Changed);
        }
        foreach (var lifetime in value.Bindings)
        {
            ResourceRetirementStorage.RequireOpen(view, lifetime, limits);
        }
        admitted = value;
    }
}
