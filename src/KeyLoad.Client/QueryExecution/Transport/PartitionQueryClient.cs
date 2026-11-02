using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads complete bounded partition queries through the authenticated SDK transport.</summary>
public static class PartitionQueryClient
{
    private const string Route = "/query/partitions";

    /// <summary>Reads a bounded query with full entity references and separate partition cuts.</summary>
    /// <param name="client">The authenticated database client.</param>
    /// <param name="request">The versioned query and complete atomic partition identities.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>A complete page or a typed transport or database problem.</returns>
    public static Task<Result<PartitionQueryPage>> PartitionQueryAsync(this KeyLoadClient client,
        PartitionQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request PartitionQueryRequest reference through its object parameter. */;
        return client.Send<PartitionQueryPage, PartitionQueryRequest>(Route, request, false, null, cancellationToken);
    }
}
