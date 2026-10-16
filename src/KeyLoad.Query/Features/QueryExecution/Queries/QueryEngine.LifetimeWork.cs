using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    internal QueryPage ExecuteBound(string principalId, QueryRequest request, TimeProvider? timeProvider,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(principalId, budget => global::KeyLoad.Query.Features.QueryExecution.ScriptDomQueryAdapter.Lower(
            global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(request.Sql, database.OperationLimitsOptions,
                budget.Check, cancellationToken), request, database.OperationLimitsOptions, executionOptions, budget,
            cancellationToken), timeProvider, cancellationToken, binding);
    }

    internal QueryPage ExecuteAstBound(string principalId, AstQueryRequest request, TimeProvider? timeProvider,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(principalId, _ => request, timeProvider, cancellationToken, binding);
    }

    internal LiveQuerySnapshot StartLiveQueryBound(string principalId, StartLiveQueryRequest request,
        TimeProvider? timeProvider, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return liveQueries.Start(principalId, request, timeProvider, cancellationToken, binding);
    }

    internal LiveQueryPage ReadLiveQueryBound(string principalId, ReadLiveQueryRequest request,
        TimeProvider? timeProvider, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return liveQueries.Read(principalId, request, timeProvider, cancellationToken, binding);
    }
    internal PartitionQueryPageV1 QueryPartitionsBound(string principalId, PartitionQueryRequestV1 request,
        PhysicalShardRecord expectedOwner, TimeProvider? timeProvider, CancellationToken cancellationToken,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Features.QueryExecution.PartitionQueryPublicExecution.Execute(this, principalId, request,
            expectedOwner, timeProvider, cancellationToken, binding);
    }
    internal Task<GraphSearchResult> SearchSqlBoundAsync(string principalId, SqlGraphSearchRequest request,
        CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return SqlGraphSearchExecutor.ExecuteAsync(database, graphSearch, principalId, request, execution, token, binding);
    }
    internal GraphShortestPathResult ShortestPathSqlBound(string principalId, SqlGraphPathRequest request,
        TimeProvider? timeProvider, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return ExecuteShortestPathSql(principalId, request, timeProvider, token, binding);
    }
    internal QueryPage ExecuteBound(string principalId, AstQueryRequest request, ReadExecutionBudget work,
        TimeProvider? timeProvider, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(principalId, _ => request, timeProvider, token, binding, work);
    }
}
