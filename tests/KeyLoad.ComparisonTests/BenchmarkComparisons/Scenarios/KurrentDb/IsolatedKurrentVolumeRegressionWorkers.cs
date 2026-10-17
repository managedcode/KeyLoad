using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKurrentVolumeRegressionWorkers
{
    private readonly int count;
    private readonly Func<int, CancellationToken, Task> operation;
    private readonly CancellationToken token;
    private readonly NativeComparisonHarnessOptions policy;
    private readonly CancellationTokenSource cancellation;
    private readonly IsolatedKurrentVolumeWorkerFailures failures = new();
    private int next = -1;

    private IsolatedKurrentVolumeRegressionWorkers(int count, Func<int, CancellationToken, Task> operation,
        IOptions<NativeComparisonHarnessOptions> harnessOptions, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(harnessOptions);
        policy = harnessOptions.Value;
        policy.Validate();
        this.count = count;
        this.operation = operation;
        this.token = token;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    internal static Task RunAsync(int count, Func<int, CancellationToken, Task> operation,
        IOptions<NativeComparisonHarnessOptions> harnessOptions, CancellationToken token)
        => new IsolatedKurrentVolumeRegressionWorkers(count, operation, harnessOptions, token).RunAsyncCore();

    private async Task RunAsyncCore()
    {
        using var lifetime = cancellation;
        var workers = Enumerable.Range(0, Math.Min(policy.KurrentVolumeMaximumWorkers, count))
            .Select(_ => RunWorkerAsync()).ToArray();
        var joined = Task.WhenAll(workers);
        try
        {
            await joined;
        }
        catch (Exception error) when (joined.IsFaulted || joined.IsCanceled)
        {
            failures.Capture(joined, error);
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
            var original = InvokeOperationAsync(index);
            try
            {
                await original;
            }
            catch (Exception error) when (original.IsFaulted || original.IsCanceled)
            {
                failures.Capture(original, error);
                await StopAfterFailureAsync();
                return;
            }
        }
    }

    private async Task InvokeOperationAsync(int index)
        => await operation(index, cancellation.Token);

    private async Task StopAfterFailureAsync()
    {
        var callbacks = CancelWorkersAsync();
        try
        {
            await callbacks;
        }
        catch (Exception callbackError) when (callbacks.IsFaulted || callbacks.IsCanceled)
        {
            failures.Capture(callbacks, callbackError);
        }
    }

    private async Task CancelWorkersAsync() => await cancellation.CancelAsync();
}
