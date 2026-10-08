using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Publishes the selected native lexical generation under the canonical acknowledged read fence.</summary>
public static class WaitForIndexClient
{
    /// <summary>Waits for complete authorized native text publication at the minimum acknowledged token.</summary>
    /// <param name="client">Authenticated SDK owner.</param>
    /// <param name="request">The canonical typed wait request.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>The actual applied cut after native lease settlement.</returns>
    public static Task<Result<WaitForIndexResult>> WaitForIndexAsync(this KeyLoadClient client, WaitForIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<WaitForIndexResult>(WaitForIndexProtocol.Route, request, false, null, cancellationToken);
    }
}
