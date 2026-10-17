using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionMeasurement(BenchmarkSessionContext context)
{
    internal async Task<BenchmarkSessionLaneReport> MeasureAsync(string id, int count, int clients,
        Func<IComparisonSession, int, CancellationToken, Task> operation, CancellationToken token,
        BenchmarkSessionStartBarrier? barrier = null, bool openDocumentSessions = true)
    {
        var lease = new BenchmarkSessionClientLease(context);
        BenchmarkSessionLaneReport? result = null;
        Exception? failure = null;
        ImmutableArray<Exception> cleanup = [];
        try
        {
            await lease.OpenAsync(clients, openDocumentSessions, token).ConfigureAwait(false);
            var execution = new BenchmarkSessionLaneExecution(context, lease, operation, count, openDocumentSessions);
            result = await execution.RunAsync(id, clients, barrier, token).ConfigureAwait(false);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            barrier?.Abort(error);
            failure = error;
        }
        finally
        {
            cleanup = await lease.CloseAsync().ConfigureAwait(false);
        }
        if (!cleanup.IsEmpty)
        {
            throw new AggregateException(BenchmarkSessionProtocol.BenchmarkSessionClientDisposalFailed,
                failure is null ? cleanup : cleanup.Add(failure));
        }
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result!;
    }
}
