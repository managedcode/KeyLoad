using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchStatisticsRequestBinding
{
    private const int FirstTerm = 0;
    private const string InvalidTerms = "The distributed statistics terms differ from the original canonical request.";

    internal static void Require(SearchRequest request, ImmutableArray<DistributedTextWitnessV1> witnesses,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget)
    {
        if (request.Text is null)
        {
            foreach (var witness in witnesses)
            {
                budget.Check();
                if (!witness.Statistics.Terms.IsEmpty)
                { throw Errors.Fail(ErrorCode.Corruption, InvalidTerms); }
            }
            return;
        }
        var execution = options.Value;
        execution.Validate();
        var original = TextRanker.ForStatistics(request.Text, request.TextField!, budget,
            execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
        foreach (var witness in witnesses)
        {
            budget.Check();
            if (witness.Statistics.Terms.Length != original.Terms.Count)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidTerms); }
            for (var index = FirstTerm; index < original.Terms.Count; index++)
            {
                budget.Check();
                if (!string.Equals(witness.Statistics.Terms[index], original.Terms[index], StringComparison.Ordinal))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidTerms); }
            }
        }
    }
}
