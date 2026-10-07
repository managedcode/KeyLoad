using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>Runs exact text, vector and hybrid ranking within one authorized read cut.</summary>
public sealed class SearchEngine
{
    private readonly DatabaseEngine database;
    private readonly ITextProjection? textProjection;
    private readonly QueryExecutionOptions execution;

    /// <summary>Creates an authorized search owner with one frozen native execution policy.</summary>
    /// <param name="database">Node-owned canonical database.</param>
    /// <param name="options">Centrally validated query and text execution budgets.</param>
    /// <param name="textProjection">Optional native derived text projection.</param>
    public SearchEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options, ITextProjection? textProjection = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        execution = options.Value;
        execution.Validate();
        this.database = database;
        this.textProjection = textProjection;
    }

    private const string UnsafeSynchronousSearch = "Await SearchAsync when native text search runs on a scheduler or synchronization context.";

    /// <summary>Returns exact fused ranks with selected documents projected by persisted policy.</summary>
    /// <param name="principalId">Persisted principal identity.</param>
    /// <param name="request">Search fields, query and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for the complete read.</param>
    /// <returns>Ranked and authorized documents.</returns>
    public RankedDocument[] Search(string principalId, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Text is not null && textProjection is not null
            && (TaskScheduler.Current != TaskScheduler.Default || SynchronizationContext.Current is not null))
        {
            throw new InvalidOperationException(UnsafeSynchronousSearch);
        }
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return SearchCore(principalId, request, budget);
    }

    /// <summary>Awaits exact search and native projection settlement without blocking the caller's execution context.</summary>
    /// <param name="principalId">Persisted principal identity.</param>
    /// <param name="request">Search fields, query and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for admission and the complete worker.</param>
    /// <returns>Ranked and authorized documents after complete lease settlement.</returns>
    public async Task<RankedDocument[]> SearchAsync(string principalId, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return await Task.Run(() => SearchCore(principalId, request, budget), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes graph scope, retrieval and expansion inside one authorized search cut.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Versioned search and graph operators.</param>
    /// <param name="cancellationToken">Caller cancellation for admission and the complete worker.</param>
    /// <returns>Exact fused hits and optional separately projected graph context.</returns>
    public async Task<GraphSearchResult> GraphSearchAsync(string principalId, GraphSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        return await GraphSearchAsync(principalId, request, budget, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<GraphSearchResult> GraphSearchAsync(string principalId, GraphSearchRequest request,
        ReadExecutionBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return await Task.Run(() => GraphSearchExecutor.Execute(database, textProjection, principalId, request, budget, execution),
            cancellationToken).ConfigureAwait(false);
    }

    private RankedDocument[] SearchCore(string principalId, SearchRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        SearchRequestValidation.Validate(request, database.Limits, false, execution);
        FilteredSearchEligibility.ValidateRequest(request.AllowedIds, database.Limits, budget);
        var similarity = request.Vector is { } vector
            ? PreparedSimilarity.Create(vector.AsMemory(), request.Space!.Metric) : null;
        FilteredSearchRequestSizer.EnsureBounded(request, database.Limits.MaxQueryBytes, budget);
        var eligibility = FilteredSearchEligibility.Create(request.AllowedIds, budget);
        return database.WithQueryView(principalId, request.Partition, request.Collection, (view, principal, resource) =>
        {
            if (request.Text is not null)
            {
                database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
            }
            if (similarity is not null)
            {
                database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
                database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
            }
            if (eligibility.IsEmpty)
            {
                return [];
            }
            budget.Check();
            var fusion = new SearchRankFusion(request.FusionConstant, request.Limit, budget);
            if (request.Text is not null)
            {
                fusion.AddBranch(FilteredSearchBranch.Apply(
                    SearchBranchExecution.RankText(database, textProjection, view, principal, resource, request, budget, execution),
                    eligibility, budget), request.TextWeight);
            }
            if (similarity is not null)
            {
                fusion.AddBranch(VectorRanker.Rank(database, view, principal, request, similarity, budget, eligibility), request.VectorWeight);
            }
            return SearchBranchExecution.ProjectSelected(database, view, principal, resource, fusion.Select(), budget);
        });
    }

    /// <summary>Computes the selected vector metric using the same SIMD and scalar grouping as search.</summary>
    /// <param name="left">Finite query vector.</param>
    /// <param name="right">Finite candidate vector.</param>
    /// <param name="metric">Typed distance metric.</param>
    /// <returns>Similarity score; Euclidean distance is negated.</returns>
    public static double Similarity(float[] left, float[] right, DistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(left);
        var prepared = PreparedSimilarity.Create(left.AsMemory(), metric);
        ArgumentNullException.ThrowIfNull(right);
        return prepared.Score(right.AsMemory());
    }
}
