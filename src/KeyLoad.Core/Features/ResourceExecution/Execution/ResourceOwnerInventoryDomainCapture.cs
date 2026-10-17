using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryDomainCapture
{
    internal static ResourceLifetimeCoverageV1? Capture(IAtomicTransaction transaction,
        PartitionRef partition, DatabaseLimits limits)
    {
        var definitions = CaptureDefinitions(transaction, partition, limits);
        if (definitions.IsDefaultOrEmpty)
        {
            ResourceOwnerInventoryCatalog.Invalidate(transaction, limits);
            return null;
        }
        var lifetimes = ImmutableArray.CreateBuilder<ResourceLifetimeV1>(definitions.Length);
        long bytes = ResourceOwnerInventoryProtocol.Zero;
        foreach (var definition in definitions)
        {
            var lifetime = ResourceLifetimeCatalog.Read(transaction, partition.TenantId,
                partition.DatabaseId, definition.Name, definition)
                ?? throw ResourceOwnerInventoryValidation.Invalid();
            bytes = checked(bytes + NativeSerialization.Measure(lifetime));
            if (bytes > limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
            }
            if (!ResourceOwnerInventoryCreation.Proven(transaction, lifetime, limits))
            {
                ResourceOwnerInventoryCatalog.Invalidate(transaction, limits);
                return null;
            }
            lifetimes.Add(lifetime);
        }
        return new(partition, lifetimes.MoveToImmutable());
    }

    private static ImmutableArray<ResourceDefinition> CaptureDefinitions(IKeyValueView view,
        PartitionRef partition, DatabaseLimits limits)
    {
        var definitions = ImmutableArray.CreateBuilder<ResourceDefinition>();
        long bytes = ResourceOwnerInventoryProtocol.Zero;
        var scanned = view.VisitRange(KeySpace.ResourcePrefix(partition.TenantId, partition.DatabaseId),
            limits.MaxScanRecords, (key, value) =>
            {
                var definition = NativeSerialization.Deserialize<ResourceDefinition>(value);
                if (!key.SequenceEqual(KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name)))
                {
                    throw ResourceOwnerInventoryValidation.Invalid();
                }
                if (definition.TransactionDomainId == partition.TransactionDomainId)
                {
                    definitions.Add(definition);
                }
                return true;
            }, observer: observed =>
            {
                if (observed > limits.MaxBatchBytes - bytes)
                {
                    throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
                }
                bytes += observed;
            });
        var result = definitions.ToImmutable();
        if (scanned.HasMore || NativeSerialization.Measure(result) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryProtocol.Capacity);
        }
        return result;
    }
}
