using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes bounded SQL shortest-path reads through the shared authenticated SDK transport.</summary>
public static class GraphPathQueryClient
{
    private const string GraphPathRoute = "/query/graph-path";

    /// <summary>Executes the SQL shortest-path profile and returns the typed graph path.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The SQL query and its bound parameters.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The bounded path and committed read cut, or a typed transport/server problem.</returns>
    public static Task<Result<GraphShortestPathResult>> ShortestPathSqlAsync(this KeyLoadClient client,
        SqlGraphPathRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request SqlGraphPathRequest reference through its object parameter. */;
        return client.Send<GraphShortestPathResult, SqlGraphPathRequest>(GraphPathRoute, request, false, null, cancellationToken);
    }
}
