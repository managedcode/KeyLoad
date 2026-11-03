using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class SearchBranchExecution
{
    private const int FirstElementIndex = 0;

    private const string MissingSelectedDocument = "A selected search document is unavailable.";
    private const string InvalidCertifiedDocument = "A certified text candidate does not match the authorized source cut.";

    internal static SearchScore[] RankText(DatabaseEngine database, ITextProjection? textProjection,
        IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        var ranker = new TextRanker(request.Text!, request.TextField!, budget, execution.TextBudgetCheckInterval,
            execution.MaximumDocumentWords, execution.MaximumWordCharacters, request.TextMatchMode);
        return RankTextPrepared(database, textProjection, view, principal, resource, request, budget, ranker);
    }

    internal static SearchScore[] RankTextPrepared(DatabaseEngine database, ITextProjection? textProjection,
        IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        ReadExecutionBudget budget, TextRanker ranker)
    {
        if (!ranker.HasTerms && request.TextIndex is null)
        {
            return ranker.Rank();
        }
        var lease = SelectedTextProjectionAdmission.Acquire(database, textProjection, view, principal, resource, request, budget);
        Exception? primaryFailure = null;
        try
        {
            var corpus = (lease as ITextCertifiedProjectionLease)?.ReadCertifiedCandidates(ranker.Terms, budget);
            return RankTextCaptured(database, view, principal, request, budget, ranker, lease, corpus);
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

    internal static SearchScore[] RankTextCaptured(DatabaseEngine database, IKeyValueView captured,
        PrincipalRecord principal, SearchRequest request, ReadExecutionBudget budget,
        TextRanker ranker, ITextProjectionLease? original, TextPostingCandidates? corpus)
    {
        if (corpus is not null)
        {
            var certified = (ITextCertifiedProjectionLease)original!;
            try
            {
                VisitCertifiedCandidates(database, captured, principal, budget, ranker, corpus);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
            {
                certified.SettleCertifiedCandidates(valid: false);
                throw;
            }
            var result = global::KeyLoad.Query.Search.Bm25CorpusReuse.Rank(
                database, captured, principal, request, budget, ranker);
            certified.SettleCertifiedCandidates(valid: true);
            return result;
        }

        if (original is not null)
        {
            ranker.AttachProjection(original);
        }
        database.VisitVisibleDocuments(captured, principal, request.Partition, request.Collection, budget, ranker.Visit);
        var scores = global::KeyLoad.Query.Search.Bm25CorpusReuse.Rank(
            database, captured, principal, request, budget, ranker);
        VerifyProjectionCandidates(original, ranker.Terms, scores, budget);
        return scores;
    }

    private static void VisitCertifiedCandidates(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ReadExecutionBudget budget, TextRanker ranker, TextPostingCandidates corpus)
    {
        foreach (var candidate in corpus.Candidates)
        {
            budget.Check();
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(candidate.Reference));
            if (document is not { Deleted: false } || document.Reference != candidate.Reference
                || document.Revision != candidate.Revision || !database.Authorization.CanReadRow(principal, document.Access))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidCertifiedDocument);
            }
            ranker.Visit(document);
        }
        ranker.UseCertifiedCorpus(corpus);
    }

    internal static RankedDocument[] ProjectSelected(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, SearchScore[] selected, ReadExecutionBudget budget, SearchRankFusion? fusion = null)
    {
        var result = new RankedDocument[selected.Length];
        for (var index = FirstElementIndex; index < selected.Length; index++)
        {
            budget.Check();
            var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(selected[index].Reference))
                ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, MissingSelectedDocument);
            result[index] = new RankedDocument(database.Project(principal, resource, document), selected[index].Score,
                fusion?.Explain(selected[index].Reference));
        }
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
