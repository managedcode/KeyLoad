using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal static class SearchCapturedExecution
{
    private const string MissingOwner = "The original search owner is missing.";

    internal static SearchStart Start(DatabaseEngine database, ITextProjection? provider,
        IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        ReadExecutionBudget budget, QueryExecutionOptions execution, PreparedSimilarity? similarity,
        FilteredSearchEligibility eligibility)
    {
        RequireFields(database, principal, resource, request, similarity);
        if (eligibility.IsEmpty)
        {
            return new(null, null, []);
        }
        var ranker = request.Text is not null ? new TextRanker(request.Text, request.TextField!, budget,
            execution.TextBudgetCheckInterval, execution.MaximumDocumentWords, execution.MaximumWordCharacters, request.TextMatchMode) : null;
        if (provider is ICapturedTextProjection capturedProjection && ranker is not null
            && (ranker.HasTerms || request.TextIndex is not null))
        {
            return new(capturedProjection.CaptureRead(view, principal, resource, request, budget), ranker, null);
        }
        return new(null, null, Rank(database, provider, view, principal, resource, request, budget,
            similarity, eligibility, ranker, null));
    }

    internal static RankedDocument[] Complete(DatabaseEngine database, SearchStart original,
        SearchRequest request, ReadExecutionBudget budget,
        PreparedSimilarity? similarity, FilteredSearchEligibility eligibility)
    {
        if (original.Completed is not null)
        {
            return original.Completed;
        }
        var owner = original.Captured ?? throw new InvalidOperationException(MissingOwner);
        Exception? primary = null;
        try
        {
            try
            {
                var source = owner.CompleteSource();
                var result = Rank(database, null, source, owner.Principal, owner.Resource, request, budget,
                    similarity, eligibility, original.Ranker, owner);
                budget.Check();
                return result;
            }
            catch (Exception error) { primary = error; throw; }
            finally { owner.Dispose(); }
        }
        catch (Exception cleanup) when (primary is not null && !(ReferenceEquals(primary, cleanup) /* KLD0041: Keep distinct captured-search primary and cleanup exception instances through CLR reference identity. */))
        {
            throw new AggregateException(primary, cleanup);
        }
    }

    private static RankedDocument[] Rank(DatabaseEngine database, ITextProjection? provider, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget,
        PreparedSimilarity? similarity, FilteredSearchEligibility eligibility,
        TextRanker? ranker, ICapturedTextRead? original)
    {
        budget.Check();
        var fusion = new SearchRankFusion(request.FusionConstant, request.Limit, budget, request.Explain);
        if (ranker is not null)
        {
            var scores = original is null
                ? SearchBranchExecution.RankTextPrepared(database, provider, view, principal, resource, request, budget, ranker)
                : SearchBranchExecution.RankTextCaptured(database, view, principal, request, budget, ranker, original.OriginalProjection);
            fusion.AddBranch(FilteredSearchBranch.Apply(scores, eligibility, budget), request.TextWeight, SearchBranchKind.Text);
        }
        if (similarity is not null)
        {
            var boundary = (double)request.FusionConstant + request.Limit;
            // Complete rankings remain necessary for hybrid fusion and ties caused by floating-point underflow.
            int? retained = request.Text is null && request.VectorWeight / boundary > request.VectorWeight / (boundary + 1d)
                ? request.Limit : null;
            fusion.AddBranch(VectorRanker.Rank(database, view, principal, request, similarity, budget, eligibility,
                retainedLimit: retained), request.VectorWeight, SearchBranchKind.Vector);
        }
        var selected = fusion.Select();
        var result = SearchBranchExecution.ProjectSelected(database, view, principal, resource, selected, budget, fusion);
        original?.VerifyTerminal(selected.Select(score => score.Reference).ToArray());
        budget.Check();
        return result;
    }

    private static void RequireFields(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition resource,
        SearchRequest request, PreparedSimilarity? similarity)
    {
        if (request.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
        }
        if (similarity is null)
        {
            return;
        }
        database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
        database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
    }
}
