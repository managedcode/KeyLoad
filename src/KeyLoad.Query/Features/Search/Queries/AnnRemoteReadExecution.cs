using KeyLoad.Core;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    internal Task<WaitForAnnIndexResult> WaitForAnnIndexAsync(string principalId,
        WaitForAnnIndexRequest request, ReadExecutionBudget originalBudget, CancellationToken cancellationToken)
        => Features.Search.AnnWaitExecution.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken);

    internal Task<AnnSearchPage> ApproximateSearchAsync(string principalId,
        ApproximateSearchRequest request, ReadExecutionBudget originalBudget, CancellationToken cancellationToken)
        => Features.Search.AnnPublicSearchExecutor.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken);
}
