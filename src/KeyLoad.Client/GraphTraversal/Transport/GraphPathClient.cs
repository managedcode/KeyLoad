using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes bounded shortest-path reads through the shared authenticated SDK transport.</summary>
public static class GraphPathClient
{
    private const string ShortestPathRoute = "/graph/shortest-path";

    /// <summary>Reads one authorized shortest path inside the requested graph partition.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The partition, graph, endpoints and traversal budgets.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The bounded path and committed read cut, or a typed transport/server problem.</returns>
    public static Task<Result<GraphShortestPathResult>> ShortestPathAsync(this KeyLoadClient client,
        GraphShortestPathRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request GraphShortestPathRequest reference through its object parameter. */;
        return client.Send<GraphShortestPathResult, GraphShortestPathRequest>(ShortestPathRoute, request, false, null, cancellationToken);
    }
}
