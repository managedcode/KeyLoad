using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryHostedSearchSettlement
{
    internal static async Task JoinAsync(Task<GrainOperationReply> operation, CancellationTokenSource caller,
        QueryHostedReadObservation observation, CancellationToken cleanup, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                _ = await operation.WaitAsync(cleanup).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (caller.IsCancellationRequested && !cleanup.IsCancellationRequested)
            {
                if (error.CancellationToken != caller.Token)
                {
                    throw;
                }
            }
        }, failures).ConfigureAwait(false);
        if (observation.Identity.RequestId != Guid.Empty)
        {
            await ServerFailureObserver.ObserveAsync(() => observation.Disposed.Task.WaitAsync(cleanup), failures)
                .ConfigureAwait(false);
        }
    }
}
