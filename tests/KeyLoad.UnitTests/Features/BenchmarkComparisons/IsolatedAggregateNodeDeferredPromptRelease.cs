namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeDeferredPromptRelease
{
    private readonly CancellationTokenSource prompt;
    private readonly Task? cancellation;
    private readonly Task? original;
    private readonly List<Exception> lateFailures = [];

    internal IsolatedAggregateNodeDeferredPromptRelease(CancellationTokenSource prompt,
        Task? cancellation, Task? original)
    {
        this.prompt = prompt;
        this.cancellation = cancellation;
        this.original = original;
    }

    internal void Start()
    {
        var release = ReleaseAsync();
        _ = release.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReleaseAsync()
    {
        if (cancellation is not null)
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => cancellation, Add);
        }
        if (original is not null)
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => original, Add);
        }
        IsolatedAggregateNodeGuardedInvocation.Capture(prompt.Dispose, Add);
    }

    private void Add(Exception failure)
    {
        if (!lateFailures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            lateFailures.Add(failure);
        }
    }
}
