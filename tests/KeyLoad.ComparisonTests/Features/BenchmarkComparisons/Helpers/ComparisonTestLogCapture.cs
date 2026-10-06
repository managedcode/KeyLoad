using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonTestLogCapture : IAsyncDisposable
{
    private const int MaximumRetainedLines = 150;
    private const int MaximumNodeLines = 2_000;
    private const int MaximumResourceBytes = 1_048_576;
    private const int MaximumLineBytes = 16_384;
    private const string StopFailureMessage = "Comparison log capture stop failed.";
    private const string ResourceLogSuffix = ".log";
    private const string ComparisonResourceName = "comparisons";
    private readonly DistributedApplication application;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ComparisonResourceLogBuffer> logs;
    private readonly ConcurrentDictionary<string, Task> logCaptures = new(StringComparer.Ordinal);
    private readonly Task capture;
    private readonly ComparisonProgressFile? progress;
    private readonly Action<string>? nativeLineObserver;
    private readonly System.Threading.Lock stopGate = new();
    private readonly System.Threading.Lock disposeGate = new();
    private Task? stopping;
    private Task? disposing;

    public ComparisonTestLogCapture(DistributedApplication application, IEnumerable<string>? selectedResources = null,
        string? progressPath = null, Action<string>? nativeLineObserver = null)
    {
        this.application = application;
        this.nativeLineObserver = nativeLineObserver;
        var names = (selectedResources ?? []).Append(ComparisonResourceName).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Any(name => name.Length is < 1 or > 100 || name.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new ArgumentException("Resource log names must be confined native identifiers.", nameof(selectedResources));
        }
        logs = names.ToDictionary(name => name, name => new ComparisonResourceLogBuffer(
            name == ComparisonResourceName ? MaximumRetainedLines : MaximumNodeLines,
            MaximumResourceBytes, MaximumLineBytes), StringComparer.Ordinal);
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
        await CollectFailureAsync(() => StopAsync(), failures);
        try { lifetime.Dispose(); }
        catch (Exception failure) { AddDistinct(failures, failure); }
        ThrowFailures(failures);
    }

    private static void AddDistinct(List<Exception> failures, Exception failure)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, failure))) { failures.Add(failure); }
    }

    private static async Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try { await operation(); }
        catch (Exception failure) { AddDistinct(failures, failure); }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        var fatal = failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(error => error is not null);
        if (fatal is not null) { ThrowFatalFirst(fatal, failures); }
        if (failures.Count == 1) { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1) { throw new AggregateException(StopFailureMessage, failures); }
    }

    private static void ThrowFatalFirst(Exception fatal, List<Exception> failures)
    {
        var ordered = new List<Exception> { fatal };
        foreach (var failure in failures) { AddDistinct(ordered, failure); }
        if (ordered.Count == 1) { ExceptionDispatchInfo.Capture(fatal).Throw(); }
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
            nativeLineObserver?.Invoke(line);
        }
        if (name == ComparisonResourceName && ComparisonProgressLine.TryFromNativeLog(line, out var marker))
        {
            progress?.Observe(marker);
        }
    }
}
