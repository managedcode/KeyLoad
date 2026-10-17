using ManagedCode.Communication;

namespace KeyLoad.Client;

internal static class QueueWorkerCompletion
{
    internal static async Task<Result<CommitReceipt>> RunAsync(KeyLoadClient client, DeliveryCommand command,
        QueueWorkerOptions policy, Action<QueueWorkerOperationObservation> observe, CancellationToken token)
    {
        var result = await InvokeAsync(client, command, observe, token).ConfigureAwait(false);
        observe(QueueWorkerObservations.Completion(command, result));
        for (var retry = QueueWorkerProtocol.InitialRetryIndex; retry < policy.UncertaintyRetries
            && QueueWorkerObservations.Unknown(result) && !token.IsCancellationRequested; retry++)
        {
            result = await InvokeAsync(client, command, observe, token).ConfigureAwait(false);
            observe(QueueWorkerObservations.Completion(command, result));
        }
        return result;
    }

    private static async Task<Result<CommitReceipt>> InvokeAsync(KeyLoadClient client, DeliveryCommand original,
        Action<QueueWorkerOperationObservation> observe, CancellationToken token)
    {
        observe(new(null, original, null, [], null, null, Stage: QueueWorkerOperationStage.Prepared));
        try
        {
            return await client.CompleteAsync(original, token).ConfigureAwait(false);
        }
        catch (Exception failure) when (QueueWorkerFailureLedger.IsNonFatal(failure))
        {
            observe(new(null, original, null, [], null, null, InvocationFailure: failure, Stage: QueueWorkerOperationStage.Threw));
            throw;
        }
        catch (Exception failure) when (!QueueWorkerFailureLedger.IsNonFatal(failure))
        {
            observe(new(null, original, null, [], null, null, InvocationFailure: failure, Stage: QueueWorkerOperationStage.Threw));
            throw;
        }
    }

}
