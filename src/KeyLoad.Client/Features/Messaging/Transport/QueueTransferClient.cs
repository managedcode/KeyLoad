using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads durable queue-transfer protocol state through the shared SDK transport.</summary>
public static class QueueTransferClient
{
    private const string InspectPath = "/v1/queues/transfers/inspect";
    private const string ReceiptPath = "/v1/queues/transfers/receipt";

    /// <summary>Reads the authorized source intent and completion state.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The complete source lane and stable transfer identity.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>The persisted source state, or null when absent.</returns>
    public static Task<Result<QueueTransferInspection?>> InspectQueueTransferAsync(this KeyLoadClient client,
        InspectQueueTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<QueueTransferInspection?>(InspectPath, request, false, null, cancellationToken);
    }

    /// <summary>Reads the committed destination receipt without asserting source completion.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The complete destination and source lanes and stable transfer identity.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>The persisted destination proof, or null when absent.</returns>
    public static Task<Result<QueueTransferReceiptInspection?>> InspectQueueTransferReceiptAsync(this KeyLoadClient client,
        InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<QueueTransferReceiptInspection?>(ReceiptPath, request, false, null, cancellationToken);
    }
}
