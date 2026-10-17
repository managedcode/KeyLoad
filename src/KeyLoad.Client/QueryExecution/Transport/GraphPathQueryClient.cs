using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes bounded SQL shortest-path reads through the shared authenticated SDK transport.</summary>
public static class GraphPathQueryClient
{
    private const string GraphPathRoute = "/v1/query/graph-path";

    /// <summary>Executes the versioned SQL shortest-path profile and returns the typed graph path.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The versioned SQL query and its bound parameters.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The bounded path and committed read cut, or a typed transport/server problem.</returns>
    public static Task<Result<GraphShortestPathResult>> ShortestPathSqlAsync(this KeyLoadClient client,
        SqlGraphPathRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<GraphShortestPathResult>(GraphPathRoute, request, false, null, cancellationToken);
    }
}
