using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchValidation
{
    private const int FirstPartition = 0;
    private const string InvalidShape = "The distributed search shape or partition set is invalid.";
    private const string SelectedGenerationUnsupported = "Selected generations require their owning search operation.";

    internal static ImmutableArray<PartitionRef> Require(DistributedSearchRequest request, IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request DistributedSearchRequest reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options IOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        budget.Check();
        var execution = options.Value;
        execution.Validate();
        if (request.Search is null || request.Partitions.IsDefaultOrEmpty
            || request.Partitions.Length > execution.MaximumPartitions
            || request.Search.Partition != request.Partitions[FirstPartition])
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidShape);
        }
        SearchRequestValidation.Validate(request.Search, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.Search.AllowedIds, budget);
        if (request.Search.TextIndex is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SelectedGenerationUnsupported);
        }
        var unique = new HashSet<PartitionRef>();
        foreach (var partition in request.Partitions)
        {
            budget.Check();
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidShape);
            }
            EngineResourceAccess.ValidatePartition(partition);
            if (!unique.Add(partition))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidShape);
            }
        }
        var ordered = request.Partitions.ToArray();
        Array.Sort(ordered, Comparer<PartitionRef>.Create(PartitionQueryOrder.ComparePartition));
        budget.Check();
        return [.. ordered];
    }
}
