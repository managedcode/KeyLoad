using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class SearchExplanationCapture(ReadExecutionBudget budget, int constant)
{
    private const int MaximumContributions = 3;
    private const long EmptyBytes = 0;
    private const string InvalidCapture = "The search branch explanation is invalid.";
    private const string CaptureExceeded = "The search explanation byte budget is exceeded.";
    private readonly Dictionary<EntityRef, List<SearchBranchContribution>> contributions = [];
    private long retainedBytes = EmptyBytes;

    internal void Record(EntityRef reference, SearchBranchContribution contribution)
    {
        budget.Check();
        var bytes = budget.MeasureResult(contribution);
        if (bytes > budget.MaximumResultBytes - retainedBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, CaptureExceeded); }
        if (!contributions.TryGetValue(reference, out var entries))
        { contributions.Add(reference, entries = []); }
        if (entries.Count >= MaximumContributions || entries.Any(item => item.Branch == contribution.Branch))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCapture); }
        retainedBytes += bytes;
        entries.Add(contribution);
    }

    internal SearchHitExplanation For(EntityRef reference)
    {
        budget.Check();
        return new(constant, contributions.TryGetValue(reference, out var entries)
            ? entries.ToImmutableArray() : []);
    }
}
