using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Search;

internal sealed class SearchExplanationCapture(ReadExecutionBudget budget, int constant)
{
    private const int MaximumContributions = 3;
    private const string InvalidCapture = "The search branch explanation is invalid.";
    private readonly Dictionary<EntityRef, List<SearchBranchContribution>> contributions = [];

    internal void Record(EntityRef reference, SearchBranchContribution contribution)
    {
        budget.Check();
        if (!contributions.TryGetValue(reference, out var entries))
        {
            contributions.Add(reference, entries = []);
        }
        if (entries.Count >= MaximumContributions || entries.Any(item => item.Branch == contribution.Branch))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCapture);
        }
        entries.Add(contribution);
    }

    internal SearchHitExplanation For(EntityRef reference)
    {
        budget.Check();
        return new(constant, contributions.TryGetValue(reference, out var entries)
            ? entries.ToImmutableArray() : []);
    }
}
