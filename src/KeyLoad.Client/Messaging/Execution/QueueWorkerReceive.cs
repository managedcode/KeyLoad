using ManagedCode.Communication;

namespace KeyLoad.Client;

internal static class QueueWorkerReceive
{
    internal static async Task<Result<ReceiveResult>> RunAsync(KeyLoadClient client, ReceiveRequest request,
        QueueWorkerOptions policy, Action<QueueWorkerOperationObservation> observe, CancellationToken token)
    {
        var result = await InvokeAsync(client, request, observe, token).ConfigureAwait(false);
        observe(QueueWorkerObservations.Receive(request, result));
        for (var retry = QueueWorkerProtocol.InitialRetryIndex; retry < policy.UncertaintyRetries
            && QueueWorkerObservations.Unknown(result) && !token.IsCancellationRequested; retry++)
        {
            result = await InvokeAsync(client, request, observe, token).ConfigureAwait(false);
            observe(QueueWorkerObservations.Receive(request, result));
        }
        return result;
    }

    private static async Task<Result<ReceiveResult>> InvokeAsync(KeyLoadClient client, ReceiveRequest original,
        Action<QueueWorkerOperationObservation> observe, CancellationToken token)
    {
        observe(new(original, null, null, [], null, null, Stage: QueueWorkerOperationStage.Prepared));
        try
        {
            return await client.ReceiveAsync(original, token).ConfigureAwait(false);
        }
        catch (Exception failure) when (QueueWorkerFailureLedger.IsNonFatal(failure))
        {
            observe(new(original, null, null, [], null, null, InvocationFailure: failure, Stage: QueueWorkerOperationStage.Threw));
            throw;
        }
        catch (Exception failure) when (!QueueWorkerFailureLedger.IsNonFatal(failure))
        {
            observe(new(original, null, null, [], null, null, InvocationFailure: failure, Stage: QueueWorkerOperationStage.Threw));
            throw;
        }
    }

}
