using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

internal sealed class ComparisonTestLogCapture : IAsyncDisposable
{
    private const int EmptyCount = 0;
    private const char DashSeparator = '-';
    private const string ResourceLogNamesMustBeConfinedExecutionIdentifiersMessage = "Resource log names must be confined execution identifiers.";
    private const int SingleItemCount = 1;

    private const string StopFailureMessage = "Comparison log capture stop failed.";
    private const string ResourceLogSuffix = ".log";
    private const string ComparisonResourceName = "comparisons";
    private readonly DistributedApplication application;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ComparisonResourceLogBuffer> logs;
    private readonly ConcurrentDictionary<string, Task> logCaptures = new(StringComparer.Ordinal);
    private readonly Task capture;
    private readonly ComparisonProgressFile? progress;
    private readonly Action<string>? lineObserver;
    private readonly System.Threading.Lock stopGate = new();
    private readonly System.Threading.Lock disposeGate = new();
    private Task? stopping;
    private Task? disposing;

    public ComparisonTestLogCapture(DistributedApplication application, IOptions<ComparisonHarnessOptions> executionOptions,
        IEnumerable<string>? selectedResources = null,
        string? progressPath = null, Action<string>? lineObserver = null)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        var execution = executionOptions.Value;
        execution.Validate();
        this.application = application;
        this.lineObserver = lineObserver;
        var names = (selectedResources ?? []).Append(ComparisonResourceName).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Any(name => name.Length == EmptyCount || name.Length > execution.MaximumResourceLogNameCharacters || name.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != DashSeparator)))
        {
            throw new ArgumentException(ResourceLogNamesMustBeConfinedExecutionIdentifiersMessage, nameof(selectedResources));
        }
        logs = names.ToDictionary(name => name, name => new ComparisonResourceLogBuffer(
            name == ComparisonResourceName ? execution.MaximumRetainedComparisonLogLines : execution.MaximumRetainedNodeLogLines,
            execution.MaximumResourceLogBytes, execution.MaximumLogLineBytes), StringComparer.Ordinal);
        var logger = application.Services.GetRequiredService<ResourceLoggerService>();
        progress = progressPath is null ? null : new ComparisonProgressFile(progressPath);
        capture = Task.Run(() => CaptureResourcesAsync(logger), CancellationToken.None);
    }

    public Task StopAsync()
    {
        lock (stopGate)
        {
            if (stopping is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                stopping = StopCoreAsync(registered.Task);
                registered.SetResult();
            }
            return stopping;
        }
    }

    private async Task StopCoreAsync(Task registered)
    {
        await registered;
        var failures = new List<Exception>();
        await CollectFailureAsync(lifetime.CancelAsync, failures);
        await CollectFailureAsync(JoinCapturesAsync, failures);
        await CollectFailureAsync(() => progress?.StopAsync() ?? Task.CompletedTask, failures);
        ThrowFailures(failures);
    }

    private async Task JoinCapturesAsync()
    {
        var failures = new List<Exception>();
        await CollectFailureAsync(() => capture, failures);
        await CollectFailureAsync(() => Task.WhenAll(logCaptures.Values), failures);
        ThrowFailures(failures);
    }

    internal bool HasCapturedLine(string resource, string marker)
        => logs.TryGetValue(resource, out var buffer)
            && buffer.Snapshot().Any(line => line.Contains(marker, StringComparison.Ordinal));

    public async Task WriteToAsync(string path)
    {
        await StopAsync();
        await File.WriteAllLinesAsync(path, logs[ComparisonResourceName].Snapshot(), CancellationToken.None);
    }

    public async Task WriteResourcesToAsync(string directory)
    {
        await StopAsync();
        foreach (var (name, buffer) in logs.Where(item => item.Key != ComparisonResourceName))
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, name + ResourceLogSuffix), buffer.Snapshot(), CancellationToken.None);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposing is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                disposing = DisposeAndJoinAsync(registered.Task);
                registered.SetResult();
            }
            return new ValueTask(disposing);
        }
    }

    private async Task DisposeAndJoinAsync(Task registered)
    {
        await registered;
        var failures = new List<Exception>();
        await CollectFailureAsync(StopAsync, failures);
        var disposal = DisposeLifetimeAsync();
        await CollectFailureAsync(() => disposal, failures);
        ThrowFailures(failures);
    }

    private async Task DisposeLifetimeAsync()
    {
        await Task.CompletedTask;
        lifetime.Dispose();
    }

    private static void AddDistinct(List<Exception> failures, Exception failure)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, failure)))
        {
            failures.Add(failure);
        }
    }

    private static Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
        => IsolatedTeardownSupport.CollectFailureAsync(operation, failures);

    private static void ThrowFailures(List<Exception> failures)
    {
        var fatal = failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(error => error is not null);
        if (fatal is not null)
        {
            ThrowFatalFirst(fatal, failures);
        }
        if (failures.Count == SingleItemCount)
        {
            ExceptionDispatchInfo.Capture(failures[EmptyCount]).Throw();
        }
        if (failures.Count > SingleItemCount)
        {
            throw new AggregateException(StopFailureMessage, failures);
        }
    }

    private static void ThrowFatalFirst(Exception fatal, List<Exception> failures)
    {
        var ordered = new List<Exception> { fatal };
        foreach (var failure in failures)
        {
            AddDistinct(ordered, failure);
        }
        if (ordered.Count == SingleItemCount)
        {
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }
        throw new AggregateException(StopFailureMessage, ordered);
    }

    private async Task CaptureResourcesAsync(ResourceLoggerService logger)
    {
        try
        {
            await foreach (var change in application.ResourceNotifications.WatchAsync(lifetime.Token))
            {
                if (logs.ContainsKey(change.Resource.Name))
                {
                    _ = logCaptures.GetOrAdd(change.ResourceId,
                        _ => CaptureLogsAsync(logger, change.ResourceId, change.Resource.Name));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureLogsAsync(ResourceLoggerService logger, string resourceId, string name)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(lifetime.Token))
            {
                foreach (var line in batch)
                {
                    CaptureLine(name, line.Content);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private void CaptureLine(string name, string line)
    {
        logs[name].Add(line);
        if (name == ComparisonResourceName)
        {
            lineObserver?.Invoke(line);
        }
        if (name == ComparisonResourceName && ComparisonProgressLine.TryFromLog(line, out var marker))
        {
            progress?.Observe(marker);
        }
    }
}
