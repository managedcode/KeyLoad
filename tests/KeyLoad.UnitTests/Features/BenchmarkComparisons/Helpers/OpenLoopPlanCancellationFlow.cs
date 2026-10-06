using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanCancellationFlow
{
    private const string MissingCancellationMessage = "The actual Node planner did not settle by cancellation.";

    internal static async Task CancelOwnedPlannerAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string outputPath, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var options = executionOptions.Value;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        Task<OpenLoopPlanNodeResult>? original = null;
        var originalObserved = false;
        var cancellationObserved = false;
        async Task ExecuteAsync()
        {
            original = OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.CancelBeforePublishOperation,
                input: null, outputPath: outputPath, keepStandardInputOpen: true, ready: ready,
                cancellationToken: cancellation.Token);
            await ready.Task.WaitAsync(options.ProcessTimeout, cancellationToken).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await original.ConfigureAwait(false);
                throw new InvalidOperationException(MissingCancellationMessage);
            }
            catch (TaskCanceledException) when (cancellation.IsCancellationRequested)
            {
                cancellationObserved = true;
            }
            finally
            {
                originalObserved = original.IsCompleted;
            }
        }
        async Task CleanupAsync()
        {
            if (original is null || originalObserved)
            {
                return;
            }
            await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
            originalObserved = original.IsCompleted;
        }
        await ServerFailureObserver.ObserveAsync(ExecuteAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(CleanupAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (!cancellationObserved || !originalObserved)
        {
            throw new InvalidOperationException(MissingCancellationMessage);
        }
    }
}
