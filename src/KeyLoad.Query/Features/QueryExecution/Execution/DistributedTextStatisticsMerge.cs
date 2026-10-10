using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedTextStatisticsMerge
{
    private const int Empty = 0;
    private const string Invalid = "The distributed statistics collection is invalid.";
    private const string Exhausted = "The distributed statistics exceed their original query grant.";

    internal static DistributedTextStatisticsV1 Combine(ImmutableArray<DistributedTextStatisticsV1> leaves,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (leaves.IsDefaultOrEmpty || leaves.Length > options.Value.MaximumPartitions)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var first = leaves[Empty] ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        if (first.Terms.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        var frequenciesBytes = checked(PartitionQueryRetention.ArrayDescriptorBytes
            + (long)sizeof(int) * first.Terms.Length);
        admission.Accept(frequenciesBytes);
        foreach (var leaf in leaves)
        {
            DistributedTextStatisticsValidation.Require(leaf, first.Terms, budget);
            admission.Accept(NativeSerialization.Measure(leaf));
        }
        var frequencies = ImmutableArray.CreateBuilder<int>(first.Terms.Length);
        for (var index = Empty; index < first.Terms.Length; index++)
        { budget.Check(); frequencies.Add(Empty); }
        var documents = Empty;
        long length = Empty;
        try
        {
            foreach (var leaf in leaves)
            {
                budget.Check();
                documents = checked(documents + leaf.DocumentCount);
                length = checked(length + leaf.TotalLength);
                for (var index = Empty; index < frequencies.Count; index++)
                { budget.Check(); frequencies[index] = checked(frequencies[index] + leaf.DocumentFrequencies[index]); }
            }
        }
        catch (OverflowException)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Exhausted); }
        var result = new DistributedTextStatisticsV1(first.Terms, documents, length, frequencies.MoveToImmutable());
        admission.Accept(NativeSerialization.Measure(result));
        DistributedTextStatisticsValidation.Require(result, first.Terms, budget);
        return result;
    }
}
