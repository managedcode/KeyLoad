using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Authenticated caller for complete canonical globally ranked distributed search.</summary>
public static class DistributedSearchClient
{
    /// <summary>Returns a complete bounded page or its original typed operation failure.</summary>
    /// <param name="client">Original authenticated SDK client and its owned transport.</param>
    /// <param name="request">Exact public partitions and existing canonical search shape.</param>
    /// <param name="cancellationToken">Original caller cancellation covering all phases and joins.</param>
    /// <returns>The complete page with scoped witnesses and an opaque statistics epoch.</returns>
    public static Task<Result<DistributedSearchPage>> DistributedSearchAsync(this KeyLoadClient client,
        DistributedSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<DistributedSearchPage>(DistributedSearchProtocol.Route, request, false, null, cancellationToken);
    }
}
