using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class GraphSearchExecutor
{
    private const int EmptyElementCount = 0;
    private const double DefaultBranchWeight = 1d;
    private const int AdjacentElementOffset = 1;
    private const int ZeroScore = 0;

    internal static GraphSearchResult Execute(DatabaseEngine database, ITextProjection? textProjection,
        string principalId, GraphSearchRequest request, ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        budget.Check();
        GraphSearchValidation.Validate(request, database.Limits, budget, database.GraphExecution, execution);
        FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
        FilteredSearchEligibility.ValidateRequest(request.Search.AllowedIds, database.Limits, budget);
        var similarity = request.Search.Vector is { } vector
            ? PreparedSimilarity.Create(vector.AsMemory(), request.Search.Space!.Metric) : null;
        var eligibility = FilteredSearchEligibility.Create(request.Search.AllowedIds, budget);
        return database.WithQueryView(principalId, request.Search.Partition, request.Search.Collection,
            (view, principal, resource) => ExecuteInCut(database, textProjection, principal, resource, view,
                request, similarity, eligibility, budget, execution));
    }

    private static GraphSearchResult ExecuteInCut(DatabaseEngine database, ITextProjection? textProjection,
        PrincipalRecord principal, ResourceDefinition resource, IKeyValueView view, GraphSearchRequest request,
        PreparedSimilarity? similarity, FilteredSearchEligibility eligibility, ReadExecutionBudget budget,
        QueryExecutionOptions execution)
    {
        AuthorizeBranches(database, principal, resource, request.Search, similarity);
        var scoped = request.Scope is null ? null : ReadWalk(database, view, principal, request.Search.Partition,
            request.Scope.Walk, budget);
        var fusion = new SearchRankFusion(request.Search.FusionConstant, request.Search.Limit, budget);
        AddTextBranch(database, textProjection, principal, resource, view, request.Search, eligibility, scoped, fusion, budget, execution);
        AddVectorBranch(database, principal, view, request.Search, similarity, eligibility, scoped, fusion, budget);
        AddRetrieverBranch(database, principal, view, request, scoped, eligibility, fusion, budget);
        var selected = fusion.Select();
        var hits = SearchBranchExecution.ProjectSelected(database, view, principal, resource, selected, budget);
        var expansion = request.Expansion is null ? null : GraphSearchExpansion.Execute(database, principal,
            view, request, selected, hits, budget);
        var result = new GraphSearchResult([.. hits], expansion);
        budget.CheckResult(result);
        return result;
    }

    private static void AuthorizeBranches(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest search, PreparedSimilarity? similarity)
    {
        if (search.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, search.TextField!);
        }
        if (similarity is not null)
        {
            database.Authorization.Require(principal, search.Partition, search.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, search.VectorField!);
        }
    }

    private static void AddTextBranch(DatabaseEngine database, ITextProjection? textProjection,
        PrincipalRecord principal, ResourceDefinition resource, IKeyValueView view, SearchRequest search,
        FilteredSearchEligibility eligibility, HashSet<EntityRef>? scoped, SearchRankFusion fusion,
        ReadExecutionBudget budget, QueryExecutionOptions execution)
    {
        if (search.Text is null || eligibility.IsEmpty)
        {
            return;
        }
        var branch = SearchBranchExecution.RankText(database, textProjection, view, principal, resource, search, budget, execution);
        if (search.TextWeight > EmptyElementCount)
        {
            fusion.AddBranch(FilterScope(FilteredSearchBranch.Apply(branch, eligibility, budget), scoped, budget),
                search.TextWeight);
        }
    }

    private static void AddVectorBranch(DatabaseEngine database, PrincipalRecord principal, IKeyValueView view,
        SearchRequest search, PreparedSimilarity? similarity, FilteredSearchEligibility eligibility,
        HashSet<EntityRef>? scoped, SearchRankFusion fusion, ReadExecutionBudget budget)
    {
        if (similarity is null || eligibility.IsEmpty)
        {
            return;
        }
        var branch = VectorRanker.Rank(database, view, principal, search, similarity, budget, eligibility);
        if (search.VectorWeight > EmptyElementCount)
        {
            fusion.AddBranch(FilterScope(branch, scoped, budget), search.VectorWeight);
        }
    }

    private static void AddRetrieverBranch(DatabaseEngine database, PrincipalRecord principal, IKeyValueView view,
        GraphSearchRequest request, HashSet<EntityRef>? scoped, FilteredSearchEligibility eligibility,
        SearchRankFusion fusion, ReadExecutionBudget budget)
    {
        if (request.Retriever is null)
        {
            return;
        }
        var reachable = ReadEntries(database, view, principal, request.Search.Partition,
            request.Retriever.Walk, budget);
        var candidates = new List<SearchScore>(reachable.Length);
        foreach (var item in reachable)
        {
            budget.Check();
            if (item.Reference.Collection == request.Search.Collection && eligibility.Allows(item.Reference.Id)
                && (scoped is null || scoped.Contains(item.Reference)))
            {
                candidates.Add(new(item.Reference, DefaultBranchWeight / (AdjacentElementOffset + item.ShortestHops)));
            }
        }
        candidates.Sort(GraphRetrieverOrder.Instance);
        if (request.Retriever.Weight > ZeroScore)
        {
            fusion.AddBranch([.. candidates], request.Retriever.Weight);
        }
    }

    private static GraphSearchReachability[] ReadEntries(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, GraphWalkSpec walk, ReadExecutionBudget budget)
        => database.ReadGraphSearchReachability(view, principal, partition, walk, budget);

    private static HashSet<EntityRef> ReadWalk(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, GraphWalkSpec walk, ReadExecutionBudget budget)
    {
        var entries = ReadEntries(database, view, principal, partition, walk, budget);
        var scoped = new HashSet<EntityRef>();
        foreach (var entry in entries)
        {
            budget.Check();
            scoped.Add(entry.Reference);
        }
        return scoped;
    }

    private static SearchScore[] FilterScope(SearchScore[] branch, HashSet<EntityRef>? scoped,
        ReadExecutionBudget budget)
    {
        if (scoped is null)
        {
            return branch;
        }
        var filtered = new List<SearchScore>(branch.Length);
        foreach (var candidate in branch)
        {
            budget.Check();
            if (scoped.Contains(candidate.Reference))
            {
                filtered.Add(candidate);
            }
        }
        return [.. filtered];
    }

    private sealed class GraphRetrieverOrder : IComparer<SearchScore>
    {
        private const int EqualOrder = 0;

        internal static GraphRetrieverOrder Instance { get; } = new();

        public int Compare(SearchScore left, SearchScore right)
        {
            var score = right.Score.CompareTo(left.Score);
            if (score != EqualOrder)
            {
                return score;
            }
            var collection = StringComparer.Ordinal.Compare(left.Reference.Collection, right.Reference.Collection);
            return collection != EqualOrder ? collection : StringComparer.Ordinal.Compare(left.Reference.Id, right.Reference.Id);
        }
    }
}
