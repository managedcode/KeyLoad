using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes versioned graph search through the shared authenticated SDK transport.</summary>
public static class GraphSearchClient
{
    private const string GraphSearchPath = "/v1/search/graph";

    /// <summary>Searches one read cut with graph scope, retrieval and separate bounded context.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">Versioned graph operators and exact search request.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>Authorized ranked hits and optional projected context.</returns>
    public static Task<Result<GraphSearchResult>> GraphSearchAsync(this KeyLoadClient client, GraphSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<GraphSearchResult>(GraphSearchPath, request, false, null, cancellationToken);
    }
}
