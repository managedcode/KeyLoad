using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryPlanFactory
{
    private const int FirstElementIndex = 0;
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;
    private const int EqualOrder = 0;

    private const int Version = 1;
    private const string InvalidPartitionsMessage = "The partition query leaf set is invalid.";
    private const string UnsupportedWindowMessage = "Partition query windows are not supported.";

    internal static PartitionQueryPlan Create(AstQueryRequest request, StoreIdentity owner,
        ImmutableArray<PartitionRef> partitions, QueryExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);
        if (request.Query.Offset != EmptyElementCount)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedWindowMessage);
        }
        var sorted = ValidateAndOrderPartitions(partitions, execution.MaximumPartitions);
        var limit = request.Query.Limit;
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafPlan>(sorted.Length);
        foreach (var partition in sorted)
        {
            leaves.Add(new(Version, partition, request with
            {
                Partition = partition,
                Cursor = null
            }, limit));
        }
        return new(Version, owner.NodeId, owner.Incarnation, owner.ReadGeneration, leaves.MoveToImmutable(), limit);
    }

    private static PartitionRef[] ValidateAndOrderPartitions(ImmutableArray<PartitionRef> partitions, int maximumPartitions)
    {
        if (partitions.IsDefaultOrEmpty || partitions.Length > maximumPartitions)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
        }

        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
            }

            EngineResourceAccess.ValidatePartition(partition);
        }
        var sorted = partitions.OrderBy(static item => item.TenantId, StringComparer.Ordinal)
            .ThenBy(static item => item.DatabaseId, StringComparer.Ordinal)
            .ThenBy(static item => item.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(static item => item.PartitionKey, StringComparer.Ordinal).ToArray();
        for (var index = FirstElementIndex; index < sorted.Length; index++)
        {
            if (index > EmptyElementCount && PartitionQueryOrder.ComparePartition(sorted[index - AdjacentElementOffset], sorted[index]) == EqualOrder)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidPartitionsMessage);
            }
        }
        return sorted;
    }
}
