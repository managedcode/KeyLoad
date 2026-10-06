using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class SearchBranchExecution
{
    private const int NoRetainedBytes = 0;
    private const int FirstElementIndex = 0;

    private const string MissingSelectedDocument = "A selected search document is unavailable.";
    private const string ResultExceeded = "The search result byte budget is exceeded.";

    internal static SearchScore[] RankText(DatabaseEngine database, ITextProjection? textProjection,
        IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        var ranker = new TextRanker(request.Text!, request.TextField!, budget, execution.TextBudgetCheckInterval,
            execution.MaximumDocumentWords, execution.MaximumWordCharacters);
        if (!ranker.HasTerms)
        {
            return ranker.Rank();
        }
        var lease = textProjection?.Acquire(TextProjectionLifecycle.CreateScope(database, principal, resource, request), budget);
        Exception? primaryFailure = null;
        try
        {
            if (lease is not null)
            {
                ranker.AttachProjection(lease);
            }
            database.VisitVisibleDocuments(view, principal, request.Partition, request.Collection, budget, ranker.Visit);
            var scores = ranker.Rank();
            VerifyProjectionCandidates(lease, ranker.Terms, scores, budget);
            return scores;
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            TextProjectionLifecycle.Dispose(lease, primaryFailure);
        }
    }

    internal static RankedDocument[] ProjectSelected(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, SearchScore[] selected, ReadExecutionBudget budget)
    {
        var result = new RankedDocument[selected.Length];
        long projectedBytes = NoRetainedBytes;
        for (var index = FirstElementIndex; index < selected.Length; index++)
        {
            budget.Check();
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(selected[index].Reference))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, MissingSelectedDocument);
            var ranked = new RankedDocument(database.Project(principal, resource, document), selected[index].Score);
            var bytes = budget.MeasureResult(ranked);
            if (bytes > database.Limits.MaxBatchBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
            }
            projectedBytes += bytes;
            result[index] = ranked;
        }
        budget.CheckResult(result);
        return result;
    }

    private static void VerifyProjectionCandidates(ITextProjectionLease? lease, IReadOnlyList<string> terms,
        SearchScore[] scores, ReadExecutionBudget budget)
    {
        if (lease is null)
        {
            return;
        }
        var references = scores.Select(score => score.Reference).Distinct().ToArray();
        lease.VerifyCandidates(terms, references, budget);
    }
}
