using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>Runs exact text, vector and hybrid ranking within one authorized read cut.</summary>
public sealed partial class SearchEngine
{
    private readonly DatabaseEngine database;
    private readonly ITextProjection? textProjection;
    private readonly QueryExecutionOptions execution;
    private readonly IOptions<QueryExecutionOptions> configuration;

    /// <summary>Creates an authorized search owner with one frozen execution execution policy.</summary>
    /// <param name="database">Node-owned canonical database.</param>
    /// <param name="options">Centrally validated query and text execution budgets.</param>
    /// <param name="textProjection">Optional execution derived text projection.</param>
    public SearchEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options, ITextProjection? textProjection = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        configuration = options;
        execution = options.Value;
        execution.Validate();
        this.database = database;
        this.textProjection = textProjection;
    }

    /// <summary>Returns exact fused ranks with selected documents projected by persisted policy.</summary>
    /// <param name="principalId">Persisted principal identity.</param>
    /// <param name="request">Search fields, query and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for the complete read.</param>
    /// <returns>Ranked and authorized documents.</returns>
    public RankedDocument[] Search(string principalId, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return SearchCore(principalId, request, budget);
    }

    /// <summary>Runs exact search in the calling Orleans worker and returns the joined execution result.</summary>
    /// <param name="principalId">Persisted principal identity.</param>
    /// <param name="request">Search fields, query and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for admission and the complete worker.</param>
    /// <returns>Ranked and authorized documents after complete lease settlement.</returns>
    public Task<RankedDocument[]> SearchAsync(string principalId, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return Task.FromResult(SearchCore(principalId, request, budget));
    }

    /// <summary>Executes graph scope, retrieval and expansion inside one authorized search cut.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Versioned search and graph operators.</param>
    /// <param name="cancellationToken">Caller cancellation for admission and the complete worker.</param>
    /// <returns>Exact fused hits and optional separately projected graph context.</returns>
    public Task<GraphSearchResult> GraphSearchAsync(string principalId, GraphSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.EvaluationClock, cancellationToken);
        return GraphSearchAsync(principalId, request, budget, cancellationToken);
    }

    internal Task<GraphSearchResult> GraphSearchAsync(string principalId, GraphSearchRequest request,
        ReadExecutionBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return Task.FromResult(GraphSearchExecutor.Execute(database, textProjection, principalId, request, budget, execution));
    }

    private RankedDocument[] SearchCore(string principalId, SearchRequest request, ReadExecutionBudget budget)
        => ExactSearchExecution.Execute(database, textProjection, execution, principalId, request, budget);

    /// <summary>Computes the selected vector metric using the same execution SIMD blocks and bounded scalar tails as search.</summary>
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
