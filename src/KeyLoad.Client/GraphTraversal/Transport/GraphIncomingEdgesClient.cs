using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Executes bounded incoming-edge reads through the shared authenticated SDK transport.</summary>
public static class GraphIncomingEdgesClient
{
    private const string IncomingRoute = "/graph/incoming";

    /// <summary>Reads the eventual reverse-adjacency projection for one authorized graph target.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The target, graph and result limit.</param>
    /// <param name="cancellationToken">Cancellation for the complete HTTP request.</param>
    /// <returns>The bounded incoming rows and committed read cut, or a typed transport/server problem.</returns>
    public static Task<Result<GraphIncomingEdgesPage>> IncomingEdgesAsync(this KeyLoadClient client,
        ReadIncomingGraphEdgesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<GraphIncomingEdgesPage>(IncomingRoute, request, false, null, cancellationToken);
    }
}
