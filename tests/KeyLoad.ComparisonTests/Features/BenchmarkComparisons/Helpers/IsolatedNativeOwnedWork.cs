using Aspire.Hosting;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeOwnedWork
{
    private const string IncompleteRunnerOwnership = "Native runner ownership was incomplete.";
    private const string RunnerCancelFailureStage = "runner-cancel";
    private const string RunnerJoinFailureStage = "runner-join";
    private const string RunnerDisposeFailureStage = "runner-lifetime-dispose";
    internal ScaleServerResourceEvidenceCollector? Collector { get; set; }
    internal Task? Observation { get; set; }
    internal Task? CollectorSettlement { get; private set; }
    internal OpenLoopNativeCancellationControl? CancellationControl { get; set; }
    private readonly System.Threading.Lock stopGate = new();
    private CancellationTokenSource? runnerLifetime;
    private Task<int>? runnerCompletion;
    private Task? runnerStop;

    internal IsolatedNativeOwnedWork(IOptions<NativeComparisonHarnessOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        ExecutionOptions = executionOptions;
    }

    internal IOptions<NativeComparisonHarnessOptions> ExecutionOptions { get; }

    internal async Task<int> RunAsync(DistributedApplication app, string resourceName, CancellationToken token)
    {
        runnerLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        runnerCompletion = AspireResourceCompletion.RunToExitAsync(app, resourceName, runnerLifetime.Token);
        if (CancellationControl is { } control)
        {
            var first = await Task.WhenAny(runnerCompletion, control.Completion);
            if (ReferenceEquals(first, control.Completion))
            { await control.Completion; }
            var exit = await runnerCompletion;
            control.RequirePublishedRequest();
            await control.Completion;
            return exit;
        }
        return await runnerCompletion;
    }

    internal Task StopRunnerAsync()
    {
        lock (stopGate)
        {
            if (runnerStop is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                runnerStop = StopRunnerAndJoinAsync(registered.Task);
                registered.SetResult();
            }
            return runnerStop;
        }
    }

    private async Task StopRunnerAndJoinAsync(Task registered)
    {
        await registered;
        var lifetime = runnerLifetime;
        var original = runnerCompletion;
        if (lifetime is null && original is null)
        { return; }
        if (lifetime is null || original is null)
        {
            throw new InvalidOperationException(IncompleteRunnerOwnership);
        }

        var failures = new IsolatedNativeTeardownFailures(null);
        var execution = ExecutionOptions;
        async Task StopOwnedAsync()
        {
            using (lifetime)
            {
                await IsolatedNativeOriginalTaskSettlement.RunAsync(lifetime.CancelAsync, RunnerCancelFailureStage, failures, execution);
                await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original, RunnerJoinFailureStage, failures, execution);
            }
        }
        await IsolatedNativeOriginalTaskSettlement.RunAsync(StopOwnedAsync, RunnerDisposeFailureStage, failures, execution);
        failures.ThrowIfAny();
    }

    internal Task StartSettlement()
    {
        if (Collector is null || Observation is null)
        {
            throw new InvalidOperationException("Server resource observation is not owned.");
        }

        return CollectorSettlement ??= Collector.CompleteAsync(Observation);
    }
}
