using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Query;

public sealed partial class SearchEngine
{
    internal Task<WaitForAnnIndexResult> WaitForAnnIndexAsync(string principalId,
        WaitForAnnIndexRequest request, ReadExecutionBudget originalBudget, CancellationToken cancellationToken)
        => global::KeyLoad.Query.Search.AnnWaitExecution.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken);

    internal Task<AnnSearchPage> ApproximateSearchAsync(string principalId,
        ApproximateSearchRequest request, ReadExecutionBudget originalBudget, CancellationToken cancellationToken)
        => global::KeyLoad.Query.Search.AnnPublicSearchExecutor.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken);
    internal Task<WaitForAnnIndexResult> WaitForAnnIndexAsync(string principalId,
        WaitForAnnIndexRequest request, ReadExecutionBudget originalBudget, AnnReadLifetimeMetadata sourceMetadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceMetadata);
        return global::KeyLoad.Query.Search.AnnWaitExecution.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken, sourceMetadata);
    }

    internal Task<AnnSearchPage> ApproximateSearchAsync(string principalId,
        ApproximateSearchRequest request, ReadExecutionBudget originalBudget, AnnReadLifetimeMetadata sourceMetadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceMetadata);
        return global::KeyLoad.Query.Search.AnnPublicSearchExecutor.ExecuteAsync(database, annProjection, principalId, request,
            configuration, originalBudget, cancellationToken, sourceMetadata);
    }
}
