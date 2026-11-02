using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Publishes the selected execution lexical generation under the canonical acknowledged read fence.</summary>
public static class WaitForIndexClient
{
    /// <summary>Waits for complete authorized execution text publication at the minimum acknowledged token.</summary>
    /// <param name="client">Authenticated SDK owner.</param>
    /// <param name="request">The canonical typed wait request.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>The actual applied cut after execution lease settlement.</returns>
    public static Task<Result<WaitForIndexResult>> WaitForIndexAsync(this KeyLoadClient client, WaitForIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request WaitForIndexRequest reference through its object parameter. */;
        return client.Send<WaitForIndexResult, WaitForIndexRequest>(WaitForIndexProtocol.Route, request, false, null, cancellationToken);
    }
}
