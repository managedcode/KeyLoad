using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    internal QueryPage ExecuteBound(string principalId, QueryRequest request, TimeProvider? timeProvider,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return Execute(principalId, budget => global::KeyLoad.Query.QueryExecution.ScriptDomQueryAdapter.Lower(
            global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(request.Sql,
                budget.Check, cancellationToken), request, executionOptions, budget,
            cancellationToken), timeProvider, cancellationToken, binding);
    }

    internal QueryPage ExecuteAstBound(string principalId, AstQueryRequest request, TimeProvider? timeProvider,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return Execute(principalId, _ => request, timeProvider, cancellationToken, binding);
    }

    internal LiveQuerySnapshot StartLiveQueryBound(string principalId, StartLiveQueryRequest request,
        TimeProvider? timeProvider, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return liveQueries.Start(principalId, request, timeProvider, cancellationToken, binding);
    }

    internal LiveQueryPage ReadLiveQueryBound(string principalId, ReadLiveQueryRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return liveQueries.Read(principalId, request, cancellationToken, binding);
    }
    internal PartitionQueryPage QueryPartitionsBound(string principalId, PartitionQueryRequest request,
        PhysicalShardRecord expectedOwner, IResourceLifetimeWorkBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return QueryExecution.PartitionQueryPublicExecution.Execute(this, principalId, request,
            expectedOwner, cancellationToken, binding);
    }
    internal Task<GraphSearchResult> SearchSqlBoundAsync(string principalId, SqlGraphSearchRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return SqlGraphSearchExecutor.ExecuteAsync(graphSearch, principalId, request, execution, token, binding);
    }
    internal GraphShortestPathResult ShortestPathSqlBound(string principalId, SqlGraphPathRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return ExecuteShortestPathSql(principalId, request, token, binding);
    }
    internal QueryPage ExecuteBound(string principalId, AstQueryRequest request, ReadExecutionBudget work,
        TimeProvider? timeProvider, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(work) /* KLD0041: BCL null validation accepts the work ReadExecutionBudget reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        return Execute(principalId, _ => request, timeProvider, token, binding, work);
    }
}
