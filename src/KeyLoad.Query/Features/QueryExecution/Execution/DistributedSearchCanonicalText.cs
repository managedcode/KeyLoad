using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchCanonicalText
{
    internal static GlobalBranchWindow Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, SearchRequest request, DistributedTextStatisticsV1 statistics,
        GlobalBranchScope scope, string sourceWindowId, QueryExecutionOptions execution,
        FilteredSearchEligibility eligibility, ReadExecutionBudget budget)
    {
        var ranker = new TextRanker(request.Text!, request.TextField!, budget,
            execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters);
        database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
        var scores = FilteredSearchBranch.Apply(ranker.Rank(statistics), eligibility, budget);
        return DistributedSearchCandidateWindow.Capture(view, scores, request.Partition, request.Collection,
            DistributedSearchBranchNames.Text, GlobalBranchKind.Text, sourceWindowId, scope, budget);
    }
}
