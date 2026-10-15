namespace KeyLoad.Client;

/// <summary>Reconciles original worker operations without invoking an application handler.</summary>
public static class QueueWorkerReconciliation
{
    /// <summary>Issues exactly the original receive or delivery command through native authenticated transport.</summary>
    /// <param name="client">Original caller-owned authenticated client.</param>
    /// <param name="original">Original operation retained by the joined worker result.</param>
    /// <param name="cancellationToken">Cancellation of this explicit native attempt.</param>
    /// <returns>The actual original-operation observation; no new GUID or callback is created.</returns>
    public static async Task<QueueWorkerOperationObservation> ReconcileQueueWorkerOperationAsync(this KeyLoadClient client,
        QueueWorkerOperationObservation original, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(original);
        if ((original.Receive is null) == (original.Command is null))
        {
            throw new ArgumentException(QueueWorkerProtocol.InvalidObservation, nameof(original));
        }
        if (original.Command is { } command)
        {
            return QueueWorkerObservations.Completion(command,
                await client.CompleteAsync(command, cancellationToken).ConfigureAwait(false));
        }
        return QueueWorkerObservations.Receive(original.Receive!,
            await client.ReceiveAsync(original.Receive!, cancellationToken).ConfigureAwait(false));
    }
}
