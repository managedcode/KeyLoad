using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads an already provisioned generation through current persisted vector authorization.</summary>
public static class ApproximateSearchClient
{
    /// <summary>Returns the complete admitted page and actual execution mode at one authorized cut.</summary>
    /// <param name="client">Authenticated actual SDK owner.</param>
    /// <param name="request">Versioned explicit generation and bounded vector scope.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>The complete page or original terminal failure.</returns>
    public static Task<Result<AnnSearchPage>> ApproximateSearchAsync(this KeyLoadClient client,
        ApproximateSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request ApproximateSearchRequest reference through its object parameter. */;
        return client.Send<AnnSearchPage, ApproximateSearchRequest>(AnnSearchProtocol.Route, request, false, null, cancellationToken);
    }
}
