using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    /// <summary>Publishes the eligible execution text generation at one acknowledged authorized read cut.</summary>
    /// <param name="principalId">Fresh persisted principal identity.</param>
    /// <param name="request">Collection, field and acknowledged minimum token.</param>
    /// <param name="cancellationToken">Original admission and complete worker cancellation.</param>
    /// <returns>The observed applied cut after joined execution generation settlement.</returns>
    public Task<WaitForIndexResult> WaitForIndexAsync(string principalId, WaitForIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        return Task.FromResult(TextIndexWaitExecution.Execute(database, textProjection, execution, readCutObserver,
            principalId, request, budget));
    }

}
