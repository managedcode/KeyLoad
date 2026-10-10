using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchValidation
{
    private const int VersionOne = 1;
    private const int FirstPartition = 0;
    private const string InvalidShape = "The distributed search shape or partition set is invalid.";
    private const string SelectedGenerationUnsupported = "Selected generations require their owning search operation.";

    internal static ImmutableArray<PartitionRef> Require(DistributedSearchRequestV1 request,
        DatabaseLimits limits, IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        if (request.Version != VersionOne || request.Search is null || request.Partitions.IsDefaultOrEmpty
            || request.Partitions.Length > execution.MaximumPartitions
            || request.Search.Partition != request.Partitions[FirstPartition])
        { throw Errors.Fail(ErrorCode.Validation, InvalidShape); }
        FilteredSearchRequestSizer.EnsureBounded(request, limits.MaxQueryBytes, budget);
        SearchRequestValidation.Validate(request.Search, limits, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.Search.AllowedIds, limits, budget);
        if (request.Search.TextIndex is not null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, SelectedGenerationUnsupported); }
        var retention = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        retention.Accept(checked(PartitionQueryRetention.RootDescriptorBytes
            + (long)request.Partitions.Length * PartitionQueryRetention.LeafDescriptorBytes));
        var unique = new HashSet<PartitionRef>();
        foreach (var partition in request.Partitions)
        {
            budget.Check();
            if (partition is null)
            { throw Errors.Fail(ErrorCode.Validation, InvalidShape); }
            DatabaseEngine.ValidatePartition(partition);
            if (!unique.Add(partition))
            { throw Errors.Fail(ErrorCode.Validation, InvalidShape); }
        }
        var ordered = request.Partitions.ToArray();
        Array.Sort(ordered, Comparer<PartitionRef>.Create(PartitionQueryOrder.ComparePartition));
        budget.Check();
        return [.. ordered];
    }
}
