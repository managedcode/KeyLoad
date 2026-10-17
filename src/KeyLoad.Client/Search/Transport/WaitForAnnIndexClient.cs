using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads an actual provisioned native ANN generation's minimum-prefix fence.</summary>
public static class WaitForAnnIndexClient
{
    /// <summary>Requires the indexed prefix without provisioning, rebuilding or retrying.</summary>
    /// <param name="client">Authenticated SDK owner.</param>
    /// <param name="request">Typed generation selector and acknowledged minimum.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>The actual indexed token and current schema/policy.</returns>
    public static Task<Result<WaitForAnnIndexResult>> WaitForAnnIndexAsync(this KeyLoadClient client,
        WaitForAnnIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<WaitForAnnIndexResult>(WaitForAnnIndexProtocol.Route, request, false, null, cancellationToken);
    }
}
