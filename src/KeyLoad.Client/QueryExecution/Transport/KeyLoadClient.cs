using System.Text.Json;
using KeyLoad.Query;
using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Executes one versioned SQL operation and returns its actual canonical JSON result.</summary>
    /// <param name="request">One Q1 SELECT or CALL and its bound arguments.</param>
    /// <param name="cancellationToken">Caller cancellation; CALL cancellation can leave an unknown write outcome.</param>
    /// <returns>The canonical selected result or a typed problem.</returns>
    public Task<Result<JsonElement>> ExecuteSqlAsync(SqlOperationRequest request, CancellationToken cancellationToken = default)
        => Send<JsonElement>(SqlOperationProtocol.Route, request,
            SqlWriteClassifier.MayWriteRequest(request, ExecutionOptions), null, cancellationToken);

    /// <summary>Executes a parsed query request.</summary>
    /// <param name="request">Typed query and requested page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>(ApiRoutes.Query, request, false, null, cancellationToken);
    /// <summary>Executes a canonical AST query request.</summary>
    /// <param name="request">Typed Q1 AST and requested page controls.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAstAsync(AstQueryRequest request, CancellationToken cancellationToken = default)
        => Send<QueryPage>(ApiRoutes.QueryAst, request, false, null, cancellationToken);
    /// <summary>Builds and executes the immutable request from a typed query builder.</summary>
    /// <typeparam name="T">Application record type used to translate the builder expressions.</typeparam>
    /// <param name="query">Typed query builder.</param>
    /// <param name="allowFullScan">Whether to permit a query without a selective predicate.</param>
    /// <param name="cursor">Optional page continuation cursor.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed query page.</returns>
    public Task<Result<QueryPage>> QueryAsync<T>(KeyLoadQuery<T> query, bool allowFullScan = false, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryAstAsync(query.ToRequest(allowFullScan, cursor), cancellationToken);
    }
    /// <summary>Reads the server's supported query capabilities.</summary>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The capability manifest.</returns>
    public Task<Result<QueryCapabilityManifest>> QueryCapabilitiesAsync(CancellationToken cancellationToken = default)
        => Send<QueryCapabilityManifest>(ApiRoutes.QueryCapabilities, null, false, null, cancellationToken);
}
