namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKurrentVolumeRegressionWorkers(
    int count, Func<int, CancellationToken, Task> operation, CancellationToken token)
{
    private const int MaximumWorkers = 16;
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
    private readonly IsolatedKurrentVolumeRegressionFailureCollector failures = new();
    private int next = -1;

    internal static Task RunAsync(int count, Func<int, CancellationToken, Task> operation, CancellationToken token)
        => new IsolatedKurrentVolumeRegressionWorkers(count, operation, token).RunAsyncCore();

    private async Task RunAsyncCore()
    {
        using var lifetime = cancellation;
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(operation);
        var workers = Enumerable.Range(0, Math.Min(MaximumWorkers, count)).Select(_ => RunWorkerAsync()).ToArray();
        var joined = Task.WhenAll(workers);
        try
        {
            await joined;
        }
        catch (Exception error) when (!IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(error))
        {
            CaptureJoinedFailures(joined, error);
        }
        catch (Exception error) when (IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(error))
        {
            CaptureJoinedFailures(joined, error);
        }
        failures.ThrowAfterWorkers();
        token.ThrowIfCancellationRequested();
    }

    private async Task RunWorkerAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            var index = Interlocked.Increment(ref next);
            if (index >= count)
            {
                return;
            }
            try
            {
                await operation(index, cancellation.Token);
            }
            catch (Exception error) when (!IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(error))
            {
                StopAfterFailure(error);
                return;
            }
            catch (Exception error) when (IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(error))
            {
                StopAfterFailure(error);
                return;
            }
        }
    }

    private void StopAfterFailure(Exception error)
    {
        failures.Capture(error);
        try
        {
            cancellation.Cancel();
        }
        catch (Exception callbackError) when (!IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(callbackError))
        {
            failures.Capture(callbackError);
        }
        catch (Exception callbackError) when (IsolatedKurrentVolumeRegressionFailureCollector.IsFatalException(callbackError))
        {
            failures.Capture(callbackError);
        }
    }

    private void CaptureJoinedFailures(Task joined, Exception error)
    {
        if (joined.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.Flatten().InnerExceptions)
            {
                failures.Capture(failure);
            }
        }
        else
        {
            failures.Capture(error);
        }
    }
}
