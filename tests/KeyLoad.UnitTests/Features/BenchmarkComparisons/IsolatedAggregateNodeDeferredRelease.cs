using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeDeferredRelease
{
    private readonly Process process;
    private readonly CancellationTokenSource? deadline;
    private readonly Task[] originals;
    private readonly Task originalJoin;
    private readonly Task actualExit;
    private readonly List<Exception> lateFailures = [];
    private bool hasPollFailure;

    internal IsolatedAggregateNodeDeferredRelease(Process process, CancellationTokenSource? deadline,
        Task[] originals, Task originalJoin, Task actualExit)
    {
        this.process = process;
        this.deadline = deadline;
        this.originals = originals;
        this.originalJoin = originalJoin;
        this.actualExit = actualExit;
    }

    internal void Start()
    {
        var release = ReleaseAsync();
        _ = release.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReleaseAsync()
    {
        await ObserveOriginalJoinAsync();
        await CaptureOriginalFailuresAsync();
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => actualExit, Add);
        await WaitForNativeExitAsync();
        DisposeDeadline();
        DisposeProcess();
    }

    private async Task ObserveOriginalJoinAsync()
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => originalJoin);
        }
        catch (AggregateException)
        {
            // Individual original task causes are retained separately; the join is observed without mirroring one.
        }
    }

    private async Task WaitForNativeExitAsync()
    {
        while (true)
        {
            try
            {
                if (IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited))
                {
                    return;
                }
            }
            catch (AggregateException envelope)
            {
                RecordPollFailure(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task CaptureOriginalFailuresAsync()
    {
        foreach (var original in originals)
        {
            if (original.IsFaulted && original.Exception is { } aggregate)
            {
                foreach (var failure in aggregate.InnerExceptions)
                {
                    Add(failure);
                }
            }
            else if (original.IsCanceled)
            {
                await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => original, Add);
            }
        }
    }

    private void DisposeDeadline()
    {
        if (deadline is not null)
        {
            IsolatedAggregateNodeGuardedInvocation.Capture(deadline.Dispose, Add);
        }
    }

    private void DisposeProcess()
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(process.Dispose, Add);
    }

    private void Add(Exception failure)
    {
        if (!lateFailures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            lateFailures.Add(failure);
        }
    }

    private void RecordPollFailure(Exception failure)
    {
        if (!hasPollFailure)
        {
            hasPollFailure = true;
            Add(failure);
        }
    }
}
