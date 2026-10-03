using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeDeferredProcessRelease
{
    private readonly Process process;
    private readonly Task originalExit;
    private readonly List<Exception> lateFailures = [];
    private bool hasPollFailure;

    internal IsolatedAggregateNodeDeferredProcessRelease(Process process, Task originalExit)
    {
        this.process = process;
        this.originalExit = originalExit;
    }

    internal void Start()
    {
        var release = ReleaseAsync();
        _ = release.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReleaseAsync()
    {
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => originalExit, Add);
        while (true)
        {
            try
            {
                if (IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited))
                {
                    break;
                }
            }
            catch (AggregateException envelope)
            {
                if (!hasPollFailure)
                {
                    hasPollFailure = true;
                    Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        IsolatedAggregateNodeGuardedInvocation.Capture(process.Dispose, Add);
    }

    private void Add(Exception failure)
    {
        if (!lateFailures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            lateFailures.Add(failure);
        }
    }
}
