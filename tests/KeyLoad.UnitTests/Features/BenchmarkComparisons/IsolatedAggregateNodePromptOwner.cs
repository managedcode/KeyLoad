using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodePromptOwner : IAsyncDisposable
{
    private const string ReleaseFailure = "Native Node prompt release failed.";
    private readonly string directory;
    private readonly List<IsolatedAggregateNodeIdentity> identities;
    private readonly Stopwatch timer;
    private readonly List<Exception> failures;
    private CancellationTokenSource? prompt;
    private Task<IsolatedAggregateNodeResult>? original;
    private Task? cancellation;
    private int cleanupStarted;

    internal IsolatedAggregateNodePromptOwner(string directory, List<IsolatedAggregateNodeIdentity> identities,
        Stopwatch timer, List<Exception> failures, CancellationToken cancellationToken)
    {
        this.directory = directory;
        this.identities = identities;
        this.timer = timer;
        this.failures = failures;
        prompt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    internal CancellationTokenSource Prompt => prompt ?? throw new ObjectDisposedException(nameof(IsolatedAggregateNodePromptOwner));
    internal OperationCanceledException? ExpectedCancellation { get; set; }

    internal void RegisterOriginal(Task<IsolatedAggregateNodeResult> task) => original = task;
    internal void RegisterCancellation(Task task) => cancellation = task;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref cleanupStarted, 1) != 0)
        {
            return;
        }
        var active = Prompt;
        var cleanup = (Cancellation: cancellation, Settled: false);
        try
        {
            cleanup = await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => IsolatedAggregateNodeTestCleanup.CleanupAsync(active, cancellation, original,
                    ExpectedCancellation, directory, identities, timer, failures));
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
        try
        {
            if (cleanup.Settled)
            {
                DisposePrompt();
            }
            else
            {
                IsolatedAggregateNodeGuardedInvocation.Invoke(
                    () => new IsolatedAggregateNodeDeferredPromptRelease(active, cleanup.Cancellation, original).Start());
                prompt = null;
            }
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
    }

    private void DisposePrompt()
    {
        try
        {
            prompt?.Dispose();
        }
        catch (Exception failure)
        {
            throw new AggregateException(ReleaseFailure, failure);
        }
        finally
        {
            prompt = null;
        }
    }
}
