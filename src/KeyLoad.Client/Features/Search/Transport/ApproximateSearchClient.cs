using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads an already provisioned generation through current persisted vector authorization.</summary>
public static class ApproximateSearchClient
{
    /// <summary>Returns the complete admitted page and actual native mode at one authorized cut.</summary>
    /// <param name="client">Authenticated actual SDK owner.</param>
    /// <param name="request">Versioned explicit generation and bounded vector scope.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>The complete page or original terminal failure.</returns>
    public static Task<Result<AnnSearchPage>> ApproximateSearchAsync(this KeyLoadClient client,
        ApproximateSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<AnnSearchPage>(AnnSearchProtocol.Route, request, false, null, cancellationToken);
    }
}
