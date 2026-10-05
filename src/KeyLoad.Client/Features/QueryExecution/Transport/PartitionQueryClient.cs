using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads complete same-owner partition queries through the authenticated SDK transport.</summary>
public static class PartitionQueryClient
{
    private const string Route = "/v1/query/partitions";

    /// <summary>Reads a bounded query with full entity references and separate partition cuts.</summary>
    /// <param name="client">The authenticated database client.</param>
    /// <param name="request">The versioned query and complete atomic partition identities.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>A complete page or a typed transport or database problem.</returns>
    public static Task<Result<PartitionQueryPageV1>> PartitionQueryAsync(this KeyLoadClient client,
        PartitionQueryRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<PartitionQueryPageV1>(Route, request, false, null, cancellationToken);
    }
}
