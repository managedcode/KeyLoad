using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPublicMapper
{
    private const int VersionOne = 1;

    private const int PageDescriptorBytes = 64;
    private const int ArrayDescriptorBytes = 32;
    private const int ReferenceBytes = 8;
    private const int RowDescriptorBytes = 32;
    private const int WitnessDescriptorBytes = 64;
    private const string MappingBudgetExceeded = "The public partition query page exceeds its retained-byte budget.";

    internal static PartitionQueryPageV1 MapPublic(PartitionQueryResultV1 result, DatabaseLimits limits,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var mappingBytes = MappingBytes(result.Rows.Length, result.Leaves.Length);
        if (mappingBytes > limits.MaxBatchBytes - result.RetainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, MappingBudgetExceeded);
        }

        var rows = MapRows(result.Rows, budget);
        var leaves = MapLeaves(result.Leaves, budget);
        var page = new PartitionQueryPageV1(VersionOne, rows, leaves, true);
        budget.CheckResult(page);
        return page;
    }

    private static long MappingBytes(int rowCount, int leafCount)
    {
        var rows = checked((long)ArrayDescriptorBytes + (long)ReferenceBytes * rowCount
            + (long)RowDescriptorBytes * rowCount);
        var leaves = checked((long)ArrayDescriptorBytes + (long)ReferenceBytes * leafCount
            + (long)WitnessDescriptorBytes * leafCount);
        return checked(PageDescriptorBytes + rows + leaves);
    }

    private static ImmutableArray<PartitionQueryRowV1> MapRows(
        ImmutableArray<PartitionQueryCandidateV1> candidates, ReadExecutionBudget budget)
    {
        var rows = ImmutableArray.CreateBuilder<PartitionQueryRowV1>(candidates.Length);
        foreach (var candidate in candidates)
        {
            budget.Check();
            rows.Add(new(candidate.Reference, candidate.Row));
        }
        return rows.MoveToImmutable();
    }

    private static ImmutableArray<PartitionQueryLeafWitnessV1> MapLeaves(
        ImmutableArray<PartitionQueryLeafResultV1> results, ReadExecutionBudget budget)
    {
        var leaves = ImmutableArray.CreateBuilder<PartitionQueryLeafWitnessV1>(results.Length);
        foreach (var result in results)
        {
            budget.Check();
            leaves.Add(new(result.Partition, result.CutPosition, result.PolicyEpoch,
                result.SchemaVersion, result.AccessPath));
        }
        return leaves.MoveToImmutable();
    }
}
