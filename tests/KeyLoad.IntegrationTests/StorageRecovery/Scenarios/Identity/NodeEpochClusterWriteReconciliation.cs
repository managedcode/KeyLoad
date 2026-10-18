namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochClusterWriteReconciliation
{
    internal static async Task<CommitReceipt> RetryRecognizedAmbiguousWriteAsync(KeyLoad.Client.KeyLoadClient writer,
        CommandRequest command, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var result = await writer.CommitAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return result.Value!;
            }
            var code = result.Problem?.ErrorCode;
            if (code is not (nameof(ErrorCode.UnknownWriteOutcome) or nameof(ErrorCode.OwnershipLost)))
            {
                throw new InvalidOperationException("The CLUSTER follower-loss write returned an unrecognized result code.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The same-ID CLUSTER follower-loss command did not reconcile before its bound.");
    }
}
