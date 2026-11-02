using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads durable queue-transfer protocol state through the shared SDK transport.</summary>
public static class QueueTransferClient
{
    private const string InspectPath = "/queues/transfers/inspect";
    private const string ReceiptPath = "/queues/transfers/receipt";

    /// <summary>Reads the authorized source intent and completion state.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The complete source lane and stable transfer identity.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>A response whose Transfer field contains the persisted source state, or null when absent.</returns>
    public static Task<Result<QueueTransferReadResult>> InspectQueueTransferAsync(this KeyLoadClient client,
        InspectQueueTransferRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request InspectQueueTransferRequest reference through its object parameter. */;
        return client.Send<QueueTransferReadResult, InspectQueueTransferRequest>(InspectPath, request, false, null, cancellationToken);
    }

    /// <summary>Reads the committed destination receipt without asserting source completion.</summary>
    /// <param name="client">The authenticated KeyLoad client.</param>
    /// <param name="request">The complete destination and source lanes and stable transfer identity.</param>
    /// <param name="cancellationToken">Cancellation for the complete request.</param>
    /// <returns>A response whose Receipt field contains the persisted destination proof, or null when absent.</returns>
    public static Task<Result<QueueTransferReceiptReadResult>> InspectQueueTransferReceiptAsync(this KeyLoadClient client,
        InspectQueueTransferReceiptRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request InspectQueueTransferReceiptRequest reference through its object parameter. */;
        return client.Send<QueueTransferReceiptReadResult, InspectQueueTransferReceiptRequest>(ReceiptPath, request, false, null, cancellationToken);
    }
}
