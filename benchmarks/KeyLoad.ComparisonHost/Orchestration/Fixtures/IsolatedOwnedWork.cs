using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal sealed class IsolatedOwnedWork : IAsyncDisposable
{
    private const string ServerResourceObservationIsNotOwnedMessage = "Server resource observation is not owned.";

    private const string IncompleteRunnerOwnership = "Execution runner ownership was incomplete.";
    private const string CaptureOwnershipFailure = "Execution log capture ownership was incomplete or already assigned.";
    private const string RunnerCancelFailureStage = "runner-cancel";
    private const string RunnerJoinFailureStage = "runner-join";
    private const string RunnerDisposeFailureStage = "runner-lifetime-dispose";
    internal ScaleServerResourceEvidenceCollector? Collector
    {
        get; set;
    }
    internal Task? Observation
    {
        get; set;
    }
    internal Task? CollectorSettlement
    {
        get; private set;
    }
    private bool captureClosed;
    internal ComparisonTestLogCapture? Capture
    {
        get; private set;
    }
    private readonly System.Threading.Lock stopGate = new();
    private CancellationTokenSource? runnerLifetime;
    private Task<int>? runnerCompletion;
    private Task? runnerStop;
    private Task? disposing;

    internal IsolatedOwnedWork(IOptions<ComparisonHarnessOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        ExecutionOptions = executionOptions;
    }

    internal IOptions<ComparisonHarnessOptions> ExecutionOptions
    {
        get;
    }

    internal void BeginCapture(DistributedApplication application, IEnumerable<string> resources,
        string progressPath, Action<string>? lineObserver = null)
    {
        if (Capture is not null || captureClosed)
        {
            throw new InvalidOperationException(CaptureOwnershipFailure);
        }
        Capture = new(application, ExecutionOptions, resources, progressPath, lineObserver);
    }

    internal Task DisposeCaptureAsync(ComparisonTestLogCapture borrowed)
    {
        if (Capture is null || !ReferenceEquals(Capture, borrowed))
        {
            throw new InvalidOperationException(CaptureOwnershipFailure);
        }
        return Capture.DisposeAsync().AsTask();
    }

    public ValueTask DisposeAsync()
    {
        lock (stopGate)
        {
            captureClosed = true;
            if (disposing is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposing = DisposeAndJoinAsync(registered.Task);
                registered.SetResult();
            }
            GC.SuppressFinalize(this);
            return new ValueTask(disposing);
        }
    }

    private async Task DisposeAndJoinAsync(Task registered)
    {
        await registered;
        try
        {
            var runnerFailure = await RuntimeComparisonFailure.ObserveAsync(StopRunnerAsync());
            var captureFailure = await RuntimeComparisonFailure.ObserveAsync(DisposeCaptureCoreAsync());
            var combined = RuntimeComparisonFailure.Combine(runnerFailure, captureFailure is null ? [] : [captureFailure]);
            if (combined is not null)
            {
                ExceptionDispatchInfo.Capture(combined).Throw();
            }
        }
        finally
        {
            runnerLifetime?.Dispose();
            runnerLifetime = null;
        }
    }

    private async Task DisposeCaptureCoreAsync()
    {
        if (Capture is not null)
        {
            await Capture.DisposeAsync();
        }
    }

    internal Task<int> RunAsync(DistributedApplication app, string resourceName, CancellationToken token)
        => RunAsync(lifetime => AspireResourceCompletion.RunToExitAsync(app, resourceName, lifetime), token);

    internal async Task<int> RunAsync(Func<CancellationToken, Task<int>> operation, CancellationToken token)
    {
        Task<int> original;
        lock (stopGate)
        {
            if (runnerLifetime is not null || runnerCompletion is not null || runnerStop is not null || captureClosed)
            {
                throw new InvalidOperationException(IncompleteRunnerOwnership);
            }
            runnerLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            original = operation(runnerLifetime.Token);
            runnerCompletion = original;
        }
        return await original;
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
        {
            return;
        }
        if (lifetime is null || original is null)
        {
            throw new InvalidOperationException(IncompleteRunnerOwnership);
        }

        var failures = new IsolatedTeardownFailures(null);
        var execution = ExecutionOptions;
        async Task StopOwnedAsync()
        {
            try
            {
                await IsolatedOriginalTaskSettlement.RunAsync(lifetime.CancelAsync, RunnerCancelFailureStage, failures, execution);
                await IsolatedOriginalTaskSettlement.RunAsync(() => original, RunnerJoinFailureStage, failures, execution);
            }
            finally
            {
                DisposeRunnerLifetime(lifetime);
            }
        }
        await IsolatedOriginalTaskSettlement.RunAsync(StopOwnedAsync, RunnerDisposeFailureStage, failures, execution);
        failures.ThrowIfAny();
    }

    private void DisposeRunnerLifetime(CancellationTokenSource borrowed)
    {
        if (runnerLifetime is null || !ReferenceEquals(runnerLifetime, borrowed))
        {
            throw new InvalidOperationException(IncompleteRunnerOwnership);
        }
        runnerLifetime.Dispose();
        runnerLifetime = null;
    }

    internal Task StartSettlement()
    {
        if (Collector is null || Observation is null)
        {
            throw new InvalidOperationException(ServerResourceObservationIsNotOwnedMessage);
        }

        return CollectorSettlement ??= Collector.CompleteAsync(Observation);
    }
}
