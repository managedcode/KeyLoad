using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryDomainCapture
{
    internal static ResourceLifetimeCoverageV1? Capture(IAtomicTransaction transaction,
        PartitionRef partition, DatabaseLimits limits)
    {
        var definitions = PartitionMoveResources.Capture(transaction, partition, limits);
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
}
